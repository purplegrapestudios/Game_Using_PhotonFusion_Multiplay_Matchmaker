using Fusion;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visual representation of a Player - the Character is instantiated by the map once it's loaded.
/// This class handles camera tracking and player movement and is destroyed when the map is unloaded.
/// (I.e. the player gets a new avatar in each map)
/// </summary>

[RequireComponent(typeof(CharacterMoveComponent))]
public class Character : NetworkBehaviour
{
	[SerializeField] private CharacterMoveComponent m_characterMoveComponent;
	[SerializeField] private Text _name;
	[SerializeField] private MeshRenderer _mesh;
	[SerializeField] private Transform m_headingTransform;

	[SerializeField] private float sensitivityX = 15f;
	[SerializeField] private float sensitivityY = 15f;
	[SerializeField] private float minimumX = -360F;
	[SerializeField] private float maximumX = 360F;
	[SerializeField] private float minimumY = -60F;
	[SerializeField] private float maximumY = 60F;
	[SerializeField] private float rotationX = 0f;
	[SerializeField] private float rotationY = 0f;
	[SerializeField] private float m_baseSpeed = 25f;
	[Networked] [SerializeField] private bool m_isJumping { get; set; }
	[Networked] [SerializeField] private float Speed { get; set; }
	[Networked] public Player Player { get; set; }
	[Networked] Vector2 m_aimDirection { get; set; }
	[Networked] Vector3 m_directionVector { get; set; }
	[Networked] private Quaternion m_lookRotation { get; set; }
	[Networked] private Vector3 m_lookDirectionForward { get; set; }
	[Networked] private Vector3 m_lookDirectionRight { get; set; }

	private Transform _camera;
	private Rigidbody rbody;

	//Data for Character Components
	private SlideOnObstacleData m_slideOnObstacleData;

	public override void Spawned()
	{
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
		rbody = GetComponent<Rigidbody>();
		SetIsJumping(true);
		m_slideOnObstacleData = new SlideOnObstacleData(Vector3.zero, 0f, false);

		if (HasInputAuthority && string.IsNullOrWhiteSpace(Player.Name.Value))
		{
			//App.FindInstance().ShowPlayerSetup();
		}
	}

	public void LateUpdate()
	{
		if (Object.HasInputAuthority)
		{
			if (_camera == null)
				_camera = Camera.main.transform;
			Transform t = _mesh.transform;
			Vector3 p = t.position;
			_camera.position = p - 10 * t.forward + 5 * Vector3.up;
			_camera.LookAt(p + 2 * Vector3.up);

		}

		// This is a little brute-force, but it gets the job done.
		// Could use an OnChanged listener on the properties instead.
		_name.text = Player.Name.Value;
		_mesh.material.color = Player.Color;
	}


	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.Escape))
		{
			Cursor.lockState = Cursor.lockState == CursorLockMode.None ? CursorLockMode.Locked : CursorLockMode.None;
			Cursor.visible = Cursor.visible ? false : true;
		}

	}

	private IEnumerator JumpCoroutineHandle;
	GroundCheckData groundCheckData;
	[SerializeField] private bool isAscending;
	Ray groundRay;
	private IEnumerator JumpCoroutine()
	{
		float startingHeight = rbody.position.y;    //Will change implementation later with ground detection as opposed to using position.y < 0
		float jumpheight = rbody.position.y + 5f;

		SetIsJumping(true);
		while (rbody.position.y < jumpheight)
		{
			isAscending = true;
			rbody.position += Runner.DeltaTime * 25 * m_headingTransform.up;
			yield return new WaitForSeconds(Runner.DeltaTime);
		}

		isAscending = false;
		//Debug.Log($"Apex height vs jumpTargetHeight: {rbody.position.y} vs {jumpheight}");

		//	groundRay = new Ray(rbody.position + new Vector3(0, transform.localScale.y / 2, 0), -transform.up * transform.localScale.y);
		//	//Ray groundRay = new Ray(rbody.position, -transform.up);
		//	groundCheckData = m_characterMoveComponent.GroundCheck(groundRay);
		//	while (!groundCheckData.isGrounded)//(rbody.position.y > 0)
		//	{
		//		rbody.position -= Runner.DeltaTime * 20f * m_headingTransform.up;
		//		groundRay = new Ray(rbody.position + new Vector3(0, transform.localScale.y / 2, 0), -transform.up * transform.localScale.y);
		//		//groundRay = new Ray(rbody.position, -transform.up);
		//		groundCheckData = m_characterMoveComponent.GroundCheck(groundRay);
		//		yield return new WaitForSeconds(Runner.DeltaTime);
		//		//if (rbody.position.y < 0)
		//		//{
		//		//	rbody.position = new Vector3(rbody.position.x, startingHeight, rbody.position.z);
		//		//	CanJump = true;
		//		//	StopCoroutine(JumpCoroutineHandle);
		//		//	JumpCoroutineHandle = null;
		//		//}
		//	}
		//	SetCanJump(true);
		StopCoroutine(JumpCoroutineHandle);
		JumpCoroutineHandle = null;
	}

	//private void OnApplicationFocus(bool focus)
	//{
	//	Cursor.lockState = focus ? CursorLockMode.Locked : CursorLockMode.None;
	//	Cursor.visible = focus ? false : true;
	//}

	public override void FixedUpdateNetwork()
	{
		if (Player && Player.InputEnabled)
        {
            if (!isAscending)
            {
				groundRay = new Ray(rbody.position + new Vector3(0, transform.localScale.y / 2, 0), -transform.up * transform.localScale.y);
				groundCheckData = m_characterMoveComponent.GroundCheck(groundRay);
				rbody.position -= Runner.DeltaTime * 20f * m_headingTransform.up;

                if (groundCheckData.isGrounded)
                {
					SetIsJumping(false);
				}
			}
        }

		if (Player && Player.InputEnabled && GetInput(out InputData data))
		{
			m_directionVector = Vector3.zero;

			//Input Update
			if (data.GetButton(ButtonFlag.LEFT))
				m_directionVector -= m_headingTransform.right;
			if (data.GetButton(ButtonFlag.RIGHT))
				m_directionVector += m_headingTransform.right;
			if (data.GetButton(ButtonFlag.FORWARD))
				m_directionVector += m_headingTransform.forward;
			if (data.GetButton(ButtonFlag.BACKWARD))
				m_directionVector -= m_headingTransform.forward;

			//Debug.DrawRay(transform.position, m_directionVector, Color.blue, 5);
			//Jump Update
			if (data.GetButton(ButtonFlag.JUMP) && !m_isJumping && JumpCoroutineHandle == null)
			{
				JumpCoroutineHandle = JumpCoroutine();
				StartCoroutine(JumpCoroutineHandle);
			}

			m_lookDirectionForward = m_lookRotation * Vector3.forward;
			m_lookDirectionRight = m_lookRotation * Vector3.right;

			//Wall Collision Detection
			m_slideOnObstacleData = m_characterMoveComponent.SlideOnObstacle(
				ray: new Ray(m_headingTransform.position + (0) * (m_directionVector * (m_headingTransform.localScale.x / 2)) + m_headingTransform.up, m_directionVector),
				rayDistance: 1.1f + Speed * Runner.DeltaTime,
				directionVector: m_directionVector,
				currentSpeed: Speed,
				upVector: m_headingTransform.up
			);
			m_directionVector = m_slideOnObstacleData.directionVector;
			Speed = m_slideOnObstacleData.isHit ? m_slideOnObstacleData.finalSpeed : m_baseSpeed;

			//Speed = m_baseSpeed;
			rbody.position += Runner.DeltaTime * Speed * m_directionVector;

			//Rotation			
			m_aimDirection = data.aimDirection;
			//rotationY = ClampAngle(m_aimDirection.y, minimumY, maximumY);		//Don't need for now. But will need later for say head movement up and down.
			Quaternion xQuaternion = Quaternion.AngleAxis(m_aimDirection.x, Vector3.up);
			//Quaternion yQuaternion = Quaternion.AngleAxis(rotationY, -Vector3.right);	//Don't need for now. But will need later for say head movement up and down.
			m_lookRotation = rbody.rotation *= xQuaternion;
			rbody.angularDrag = 0;
			rbody.velocity = Vector3.zero;
			return;
		}

	}

	private float ClampAngle(float angle, float min, float max)
	{
		if (angle > -360f)
			angle += 360f;
		if (angle < 360f)
			angle -= 360f;
		return Mathf.Clamp(angle, min, max);
	}

	//RPCs

	//[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
	public void SetIsJumping(bool isJumping)
	{
		m_isJumping = isJumping;
		rbody.position = new Vector3(rbody.position.x, groundCheckData.groundedYPos, rbody.position.z);
	}

}
using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct SlideOnObstacleData
{
    public Vector3 directionVector;
    public bool isHit;
    public float finalSpeed;

    public SlideOnObstacleData(Vector3 directionVector, float finalSpeed, bool isHit) : this()
    {
        this.directionVector = directionVector;
        this.finalSpeed = finalSpeed;
        this.isHit = isHit;
    }

    public SlideOnObstacleData Data
    {
        get
        {
            return new SlideOnObstacleData(directionVector, finalSpeed, isHit);
        }
        set
        {
            directionVector = value.directionVector;
            finalSpeed = value.finalSpeed;
            isHit = value.isHit;
        }
    }
}

public struct GroundCheckData
{
    public bool isGrounded;
    public float groundedYPos;

    public GroundCheckData(bool isGrounded, float groundedYPos)
    {
        this.isGrounded = isGrounded;
        this.groundedYPos = groundedYPos;
    }

}

public class CharacterMoveComponent : NetworkBehaviour
{
    [Header("Layers for Groundcheck (Player / Flight Runner)")] public LayerMask RayCastLayersToHit;
    [Header("Character Move Data")] public CharacterMoveData m_moveData;
    private Transform Transform;
    private Transform MainCameraTransform;
    private Rigidbody Rigidbody;
    private CharacterComponents PlayerObjectComponents;
    private CharacterAnimation PlayerAnimation;
    private CharacterHealthComponent m_characterHealthComponent;
    private Character m_character;
    private System.Action<Vector3> m_crouchCameraCallback;
    private System.Action<bool> m_crouchHitboxCallback;
    private bool m_initialized;

    [Networked] [SerializeField] public NetworkBool NetworkedFloorDetected { get; set; }
    [Networked] [SerializeField] public NetworkBool NetworkedIsCrouched { get; set; }
    [Networked] [SerializeField] public Vector3 NetworkedVelocity { get; set; }

    /// <summary>
    /// //////
    /// </summary>

    private void ResetRigidBodyState()
    {
        Rigidbody.velocity = Vector3.zero;
        Rigidbody.angularVelocity = Vector3.zero;
        Rigidbody.angularDrag = 0;
        Rigidbody.drag = 0;
        Rigidbody.inertiaTensor = Vector3.zero;
        Rigidbody.inertiaTensorRotation = Quaternion.identity;
    }

    public void InitCharacterMovement(CharacterHealthComponent characterHealthComponent, System.Action<Vector3> crouchCameraCallback, System.Action<bool> crouchHitboxCallback)
    {
        m_characterHealthComponent = characterHealthComponent;
        m_crouchCameraCallback = crouchCameraCallback;
        m_crouchHitboxCallback = crouchHitboxCallback;

        m_character = GetComponent<Character>();
        Rigidbody = GetComponent<Rigidbody>();
        ResetRigidBodyState();

        Transform = GetComponent<Transform>();
        Rigidbody.rotation = Quaternion.identity;
        PlayerObjectComponents = GetComponent<CharacterComponents>();
        MainCameraTransform = m_character.CharacterCamera.transform;
        PlayerAnimation = GetComponent<CharacterAnimation>();

        m_moveData.moveCmd = new CharacterMoveData.MoveCmd();
        m_moveData.V_GroundHits = new RaycastHit[255];
        m_moveData.V_CeilingHits = new RaycastHit[255];
        m_moveData.V_WallHits = new RaycastHit[255];
        m_initialized = true;
    }

    [SerializeField] private Vector3 m_directionVector;
    private Vector3 CrossResult;
    private float DotResult;
    private Vector3 CorrectedDirection;
    private Transform HitTransform;
    private Ray directionRay;
    private RaycastHit[] hits;

    private const string kLabelMouseX = "Mouse X";
    private const string kTagLevel = "Level";
    private const string kTagBouncePad = "BouncePad";
    private const string kTagSpeedRamp = "SpeedRamp";

    #region Character Movement Update

    private void UpdateMovement()
    {
        //OnRotate();                 // 1) Rotate Player along Y Axis

        OnGroundCheck(transform.localScale.y + .01f * transform.localScale.y);        // 3) Detect Collision Against Ground
        OnCeilingCheck();           // 4) Detect Collision Against Ceiling
        OnQueueJump();              // 5) Check Jump Input
        OnToggleCrouch();                 // 6) Crouching

        if (m_moveData.V_IsGrounded)
        {
            if (m_moveData.V_IsLanded == false)
                m_moveData.V_IsLanded = true;
            m_moveData.V_IsJumping = false;
            OnGroundMove();
        }
        else if (!m_moveData.V_IsGrounded)
        {
            OnAirMove();
        }

        OnCollisionWall(2.5f);      // 2) Detect Collision Against Wall
        OnLimitSpeed();
        UpdateNetworkedVariables(); //Used for animation
    }

    private void UpdateNetworkedVariables()
    {
        //Used for animation
        NetworkedFloorDetected = m_moveData.V_IsFloorDetected;
        NetworkedVelocity = m_moveData.V_PlayerVelocity;
    }

    public override void FixedUpdateNetwork()
    {
        if (!m_initialized) return;
        if (!m_characterHealthComponent.NetworkedIsAlive) return;

        ResetRigidBodyState();

        if (m_character.Player && m_character.Player.InputEnabled && GetInput(out InputData data))
        {
            m_moveData.V_RotationY += m_character.FixedAimDirDelta.x * m_moveData.V_MouseSensitivity * Time.timeScale;
            OnRotate(m_moveData.V_RotationY);

            UpdateMovement();
            ResetRigidBodyState();

            if (m_slopeRayDist > 0 && m_moveData.V_IsGrounded)
            {
                //Walking on Walkable Surface
                var vel = m_moveData.V_PlayerVelocity;
                var moveVel = new Vector3(vel.x, 0, vel.z);
                var yVel = new Vector3(0, vel.y, 0);
                var projectedMoveVel = Vector3.ProjectOnPlane(moveVel, m_moveData.V_SlopeHit.normal);
                var combinedVel = (projectedMoveVel + yVel);
                Rigidbody.MovePosition(Rigidbody.position + combinedVel * m_character.Runner.DeltaTime);
            }
            else if (m_slopeRayDist == 0)
            {
                //Slope Too Steep
                m_moveData.V_PlayerVelocity = new Vector3(0, m_moveData.V_PlayerVelocity.y, 0);
                var projectedMoveVel = Vector3.ProjectOnPlane(m_moveData.V_PlayerVelocity, m_moveData.V_SlopeHit.normal);
                Rigidbody.MovePosition(Rigidbody.position + projectedMoveVel * m_character.Runner.DeltaTime);
            }
            else
            {
                //Air Move
                Rigidbody.MovePosition(Rigidbody.position + m_moveData.V_PlayerVelocity * m_character.Runner.DeltaTime);
            }

        }
    }

    public override void Render()
    {
        if (!m_initialized) return;
        if (!m_characterHealthComponent.NetworkedIsAlive) return;

        if (m_character.Player && m_character.Player.InputEnabled)
        {
            m_moveData.V_RotationY += m_character.CachedAimDirDelta.x;
            OnRotate(m_moveData.V_RotationY);
        }
    }

    #endregion Character Movement Update

    #region Character Movement Functions

    private void OnRotate(float rotationY)
    {
        Transform.rotation = Quaternion.Euler(0, rotationY, 0);
    }

    private void OnCollisionWall(float dist) {
        m_moveData.V_Ray_Velocity = new Ray(Transform.position, m_moveData.V_PlayerVelocity);

        m_moveData.V_WishDirectionCollision = Vector3.zero;

        if (Physics.RaycastNonAlloc(m_moveData.V_Ray_Velocity, m_moveData.V_WallHits, dist, RayCastLayersToHit) > 0)
        {
            Debug.DrawRay(transform.position, m_moveData.V_PlayerVelocity, Color.red);
            foreach (RaycastHit collisionHit in m_moveData.V_WallHits)
            {
                if (collisionHit.collider == null) return;
                if (collisionHit.transform.CompareTag(kTagLevel))
                {
                    CrossResult = Vector3.Cross(m_moveData.V_WallHits[0].normal, transform.up);
                    DotResult = Vector3.Dot(CrossResult, m_moveData.V_PlayerVelocity);
                    if (DotResult < 0)
                    {
                        CorrectedDirection = CrossResult * -1;
                    }
                    else
                    {
                        CorrectedDirection = CrossResult;
                    }

                    //We are not Modifying the Rotation here, rather the m_directionVector is used to Calculate Movement Direction. So player would move sideways along wall, if he ran into it.
                    //Using state.DirectionVector here, will change and synchronize the values across the network. But we do NOT want it to change in this situation.
                    var yCorrectionVel = new Vector3(0, m_moveData.V_PlayerVelocity.y, 0);
                    m_moveData.V_PlayerVelocity = CorrectedDirection + yCorrectionVel;
                    Debug.DrawRay(transform.position, m_moveData.V_PlayerVelocity, Color.blue);
                    float speedDotResult = Vector3.Dot(transform.forward, m_moveData.V_WallHits[0].normal);
                    //m_moveData.V_PlayerVelocity = speedDotResult < 0 ? m_moveData.V_PlayerVelocity *= 1 + speedDotResult : m_moveData.V_PlayerVelocity;
                    //m_moveData.V_WishDirectionCollision = m_moveData.V_PlayerVelocity.normalized;
                }

                if (collisionHit.transform.CompareTag(kTagBouncePad))
                {
                    //Debug.Log("HitBouncePadWall");
                    m_moveData.V_IsGrounded = false;
                    //_playerVelocity.y = jumpSpeed * 4;
                    if (!m_moveData.V_IsBouncePadWallDetected)
                    {
                        m_moveData.V_BoostVelocity = collisionHit.normal * 50 + m_moveData.P_JumpSpeed * Transform.up;
                        m_moveData.V_PlayerVelocity = m_moveData.V_BoostVelocity;
                        m_moveData.V_IsBouncePadWallDetected = true;
                    }

                }
                else
                {
                    m_moveData.V_IsBouncePadWallDetected = false;
                }

            }

        }
        else
        {
            m_moveData.V_IsBouncePadWallDetected = false;
        }

    }

    [SerializeField] private float m_maxSlopeAngle = 80;
    [SerializeField] private float m_maxSlopeRayDist;
    [SerializeField] private float m_slopeRayDist;
    private void OnGroundCheck(float distance)
    {
        if (m_moveData.V_KnockBackOverride)
        {
            m_moveData.V_IsGrounded = false;
            return;
        }

        m_moveData.V_TempJumpSpeed = m_moveData.P_JumpSpeed;
        m_moveData.V_TempRampJumpSpeed = m_moveData.P_JumpSpeed * 10;
        if (m_moveData.V_WishJump) { m_moveData.V_TempJumpSpeed *= 10; m_moveData.V_TempRampJumpSpeed *= 2; }
        m_moveData.V_Rays_Ground[0] = new Ray(Transform.position, -Transform.up);
        //m_moveData.V_Rays_Ground[1] = new Ray(Transform.position + Transform.forward, -Transform.up);
        //m_moveData.V_Rays_Ground[2] = new Ray(Transform.position - Transform.forward, -Transform.up);
        //m_moveData.V_Rays_Ground[3] = new Ray(Transform.position + Transform.right, -Transform.up);
        //m_moveData.V_Rays_Ground[4] = new Ray(Transform.position - Transform.right, -Transform.up);
        //DO NOT WANT NOT BEING ABLE TO JUMP UNEXPECTEDLY. MUST RAYCAST AT LEAST >= PLAYER HEIGHT=4

        //scan an additional height of (between 0 - 1 * PlayerHeight) depending on MaxAngle of traversable slope;
        m_maxSlopeRayDist = distance + Mathf.Sin(m_maxSlopeAngle * Mathf.PI / 180) * transform.localScale.y;
        m_slopeRayDist = Mathf.Infinity;
        foreach (Ray ray in m_moveData.V_Rays_Ground)
        {
            if (Physics.Raycast(ray, out m_moveData.V_SlopeHit, m_maxSlopeRayDist, RayCastLayersToHit)) {
                float slopeAngle = Vector3.Angle(transform.up, m_moveData.V_SlopeHit.normal);
                if (slopeAngle < m_maxSlopeAngle)
                {
                    m_moveData.V_Rays_Ground[0] = new Ray(Transform.position, -m_moveData.V_SlopeHit.normal);
                    //m_moveData.V_Rays_Ground[1] = new Ray(Transform.position + Transform.forward - Transform.right, -m_moveData.V_SlopeHit.normal);
                    //m_moveData.V_Rays_Ground[3] = new Ray(Transform.position + Transform.forward + Transform.right, -m_moveData.V_SlopeHit.normal);
                    //m_moveData.V_Rays_Ground[2] = new Ray(Transform.position - Transform.forward + Transform.right, -m_moveData.V_SlopeHit.normal);
                    //m_moveData.V_Rays_Ground[4] = new Ray(Transform.position - Transform.forward - Transform.right, -m_moveData.V_SlopeHit.normal);
                    Debug.DrawRay(Transform.position, -m_moveData.V_SlopeHit.normal * m_maxSlopeRayDist, Color.blue);
                    //distance += Mathf.Sin(slopeAngle * Mathf.PI / 180) * transform.localScale.y;
                    m_slopeRayDist = distance;
                    break;
                }
                else
                {
                    m_slopeRayDist = 0;
                }

            }
        }

        if (!GroundRayCast(m_moveData.V_Rays_Ground, distance))
        {
            m_moveData.V_IsFloorDetected = false;
            m_moveData.V_IsGrounded = false;
            if (m_moveData.V_PlayerVelocity.y < -100f)
            {
                m_moveData.V_IsLanded = false;
            }
        }
    }

    private bool GroundRayCast(Ray[] rays, float dist)
    {
        foreach (Ray ray in rays)
        {
            if (Physics.RaycastNonAlloc(ray, m_moveData.V_GroundHits, dist, RayCastLayersToHit) > 0)
            {
                foreach (RaycastHit hit in m_moveData.V_GroundHits)
                {
                    Debug.DrawRay(Transform.position, -Transform.up * dist, Color.red, 0.1f);

                    m_moveData.V_IsFloorDetected = true;
                    m_moveData.V_GroundHit = hit;
                    m_moveData.V_GroundHitTransform = m_moveData.V_GroundHit.transform;
                    m_moveData.v_GroundHitTransformName = m_moveData.V_GroundHitTransform.name;

                    if (hit.transform.CompareTag(kTagSpeedRamp))
                    {
                        m_moveData.V_IsGrounded = false;
                        if (!m_moveData.V_IsBoosted)
                        {
                            m_moveData.V_BoostVelocity = Vector3.Cross(m_moveData.V_GroundHit.normal, -m_moveData.V_GroundHit.transform.right) * 75 + m_moveData.V_TempRampJumpSpeed * Transform.up / 10;
                            m_moveData.V_PlayerVelocity = m_moveData.V_BoostVelocity;
                            m_moveData.V_IsBoosted = true;
                            m_moveData.V_IsSliding = true;
                        }
                    }
                    else if (hit.transform.CompareTag(kTagBouncePad))
                    {
                        m_moveData.V_IsGrounded = false;
                        if (!m_moveData.V_IsBoosted)
                        {
                            m_moveData.V_BoostVelocity = m_moveData.V_GroundHit.normal * 50 + m_moveData.V_TempJumpSpeed * Transform.up / 10 + new Vector3(m_moveData.V_PlayerVelocity.x, 0, m_moveData.V_PlayerVelocity.z);
                            m_moveData.V_PlayerVelocity = m_moveData.V_BoostVelocity;
                            m_moveData.V_IsBoosted = true;
                            m_moveData.V_IsSliding = false;
                        }

                    }
                    else
                    {
                        if (!m_moveData.V_IsBouncePadWallDetected)// && m_slopeRayDist > 0)
                        {
                            m_moveData.V_IsBoosted = false;
                            m_moveData.V_IsGrounded = true;
                            m_moveData.V_PlayerVelocity.y = 0;
                        }
                        if (m_moveData.V_IsSliding) m_moveData.V_IsSliding = false;
                    }
                    return true;
                }

            }
        }
        return false;
    }

    private void OnCeilingCheck()
    {
        m_moveData.V_Ray_Ceiling = new Ray(Transform.position, Transform.up);

        //DO NOT WANT NOT BEING ABLE TO JUMP UNEXPECTEDLY. MUST RAYCAST AT LEAST >= PLAYER HEIGHT=4
        if (Physics.RaycastNonAlloc(m_moveData.V_Ray_Ceiling, m_moveData.V_CeilingHits, transform.localScale.y + .1f, RayCastLayersToHit) > 0)
        {
            foreach (RaycastHit hit in m_moveData.V_CeilingHits)
            {
                m_moveData.V_IsHitCeiling = true;
                if (m_moveData.V_PlayerVelocity.y > 0)
                    m_moveData.V_PlayerVelocity.y = 0;
            }
        }
        else
        {
            m_moveData.V_IsHitCeiling = false;
        }
    }

    private void OnSetMovementDir()
    {
        if (m_moveData.V_IsSliding) return;


        if (m_character.m_inputForward.Equals(true))
        {
            //Debug.Log($"MoveCmd Forward: {m_moveData.moveCmd.ForwardMove}, {m_moveData.V_PlayerVelocity}");
            m_moveData.moveCmd.ForwardMove = 1;
        }
        else if (m_character.m_inputBack.Equals(true))
        {
            //Debug.Log($"MoveCmd Forward: {m_moveData.moveCmd.ForwardMove}, {m_moveData.V_PlayerVelocity}");
            m_moveData.moveCmd.ForwardMove = -1;
        }
        else
        {
            m_moveData.moveCmd.ForwardMove = 0;
        }
        if (m_character.m_inputLeft.Equals(true))
        {
            //Debug.Log($"MoveCmd Right: {m_moveData.moveCmd.RightMove}, {m_moveData.V_PlayerVelocity}");
            m_moveData.moveCmd.RightMove = -1;
        }
        else if (m_character.m_inputRight.Equals(true))
        {
            //Debug.Log($"MoveCmd Right: {m_moveData.moveCmd.RightMove}, {m_moveData.V_PlayerVelocity}");
            m_moveData.moveCmd.RightMove = 1;
        }
        else
        {
            m_moveData.moveCmd.RightMove = 0;
        }

        m_moveData.P_MoveSpeed = 20;
        //cmd.forwardmove = Input.GetAxis("Vertical");
        //cmd.rightmove   = Input.GetAxis("Horizontal");
    }

    private void OnApplyFriction(float t)
    {
        if (m_moveData.V_IsSliding) return;

        Vector3 vec = m_moveData.V_PlayerVelocity;
        float speed;
        float newspeed;
        float control;
        float drop;

        vec.y = 0.0f;
        speed = vec.magnitude;
        drop = 0.0f;

        /* Only if the player is on the ground then apply friction */
        if (m_moveData.V_IsGrounded)
        {
            control = speed < m_moveData.P_RunDeacceleration ? m_moveData.P_RunDeacceleration : speed;
            drop = control * m_moveData.P_Friction * m_character.Runner.DeltaTime * t;// Time.deltaTime * t;
        }

        newspeed = speed - drop;
        m_moveData.V_PlayerFriction = newspeed;
        if (newspeed < 0)
            newspeed = 0;
        if (speed > 0)
            newspeed /= speed;

        m_moveData.V_PlayerVelocity.x *= newspeed;
        // playerVelocity.y *= newspeed;
        m_moveData.V_PlayerVelocity.z *= newspeed;
    }

    private void OnAccelerate(Vector3 wishdir, float wishspeed, float accel)
    {
        float addspeed;
        float accelspeed;
        float currentspeed;

        if (m_moveData.V_PlayerVelocity.magnitude >= 100)
        {
            return;
        }


        currentspeed = Vector3.Dot(m_moveData.V_PlayerVelocity, wishdir);
        addspeed = wishspeed - currentspeed;
        if (addspeed <= 0)
            return;
        accelspeed = accel * m_character.Runner.DeltaTime * wishspeed;// Time.deltaTime * wishspeed;
        if (accelspeed > addspeed)
            accelspeed = addspeed;

        m_moveData.V_PlayerVelocity.x += accelspeed * wishdir.x;
        m_moveData.V_PlayerVelocity.z += accelspeed * wishdir.z;
    }

    private void OnLimitSpeed()
    {
        if (m_moveData.V_IsBoosted || m_moveData.V_IsSliding)
            return;

        if (m_moveData.V_PlayerVelocity.x < -m_moveData.P_MaxSpeed)
            m_moveData.V_PlayerVelocity.x = -m_moveData.P_MaxSpeed;

        if (m_moveData.V_PlayerVelocity.x > m_moveData.P_MaxSpeed)
            m_moveData.V_PlayerVelocity.x = m_moveData.P_MaxSpeed;

        if (m_moveData.V_PlayerVelocity.z < -m_moveData.P_MaxSpeed)
            m_moveData.V_PlayerVelocity.z = -m_moveData.P_MaxSpeed;

        if (m_moveData.V_PlayerVelocity.z > m_moveData.P_MaxSpeed)
            m_moveData.V_PlayerVelocity.z = m_moveData.P_MaxSpeed;

    }

    private void OnQueueJump()
    {
        if (m_character.m_inputJump)
        {
            if (NetworkedIsCrouched)
            {
                NetworkedIsCrouched = false;
                return;
            }

            m_moveData.V_WishJump = true;
        }
        else
        {
            m_moveData.V_WishJump = false;
        }
    }

    private void OnToggleCrouch()
    {
        if (!m_moveData.V_IsGrounded)
        {
            NetworkedIsCrouched = false;
            return;
        }

        if (m_character.m_inputCrouch)
        {
            NetworkedIsCrouched = !NetworkedIsCrouched;
        }
        m_crouchCameraCallback(NetworkedIsCrouched ? new Vector3(1, 0, -2).normalized : new Vector3(1, 1, -2).normalized);
        m_crouchHitboxCallback(NetworkedIsCrouched);
    }

    private void OnGroundMove()
    {
        Vector3 wishdir;

        //set airjump to false again to allow an Extra Jump in Airmove()
        m_moveData.V_Airjump = false;

        // Do not apply friction if the player is queueing up the next jump
        if (!m_moveData.V_WishJump)
        {
            if (m_moveData.V_RaycastFloorType == 1)
            {
                OnApplyFriction(0f);
            }
            else
            {
                OnApplyFriction(1f);
            }
        }
        else
        {
            OnApplyFriction(0);
        }

        float scale = m_moveData.CmdScale();

        OnSetMovementDir();

        wishdir = new Vector3(m_moveData.moveCmd.RightMove, 0, m_moveData.moveCmd.ForwardMove);
        wishdir = Transform.TransformDirection(wishdir);
        wishdir.Normalize();
        m_moveData.V_MoveDirectionNorm = wishdir;
        float wishspeed = wishdir.magnitude;
        wishspeed *= m_moveData.P_MoveSpeed;

        OnAccelerate(wishdir, wishspeed, m_moveData.P_RunAcceleration);

        // Reset the gravity velocity		
        m_moveData.V_PlayerVelocity.y = 0;

        //SingleJump
        if (m_moveData.V_WishJump && !m_moveData.V_IsHitCeiling)
        {
            m_moveData.V_IsJumping = true;
            m_moveData.V_PlayerVelocity.y = m_moveData.P_JumpSpeed;
            m_moveData.V_IsDoubleJumping = false;

            m_moveData.V_WishJump = false;

            m_moveData.V_IsLanded = false;
        }
    }

    private void OnAirMove()
    {
        Vector3 wishdir;
        float wishvel = m_moveData.P_AirAcceleration;
        float accel;

        float scale = m_moveData.CmdScale();

        m_moveData.V_KnockBackOverride = false;
        m_moveData.V_IsBoosted = false;

        OnSetMovementDir();

        wishdir = new Vector3(m_moveData.moveCmd.RightMove, 0, m_moveData.moveCmd.ForwardMove);
        wishdir = Transform.TransformDirection(wishdir);

        float wishspeed = wishdir.magnitude;
        wishspeed *= m_moveData.P_MoveSpeed;

        wishdir.Normalize();
        m_moveData.V_MoveDirectionNorm = wishdir;
        wishspeed *= scale;

        // CPM: Aircontrol
        float wishspeed2 = wishspeed;
        if (Vector3.Dot(m_moveData.V_PlayerVelocity, wishdir) < 0)
            accel = m_moveData.P_AirDeacceleration;
        else
            accel = m_moveData.P_AirAcceleration;
        // If the player is ONLY strafing left or right
        if (m_moveData.moveCmd.ForwardMove == 0 && m_moveData.moveCmd.RightMove!= 0)
        {
            if (wishspeed > m_moveData.P_SideStrafeSpeed)
                wishspeed = m_moveData.P_SideStrafeSpeed;
            accel = m_moveData.P_SideStrafeAcceleration;
        }

        OnAccelerate(wishdir, wishspeed, accel);
        if (m_moveData.P_AirControl > 0)
        {
            OnAirControl(wishdir, wishspeed2);
        }
        // Apply gravity
        m_moveData.V_PlayerVelocity.y -= m_moveData.P_Gravity * m_character.Runner.DeltaTime;// Time.deltaTime;
    }

    // Updates PlayerVelocity based on AirControl Parameter Values
    private void OnAirControl(Vector3 wishdir, float wishspeed)
    {
        float zspeed;
        float speed;
        float dot;
        float k;

        // Can't control movement if not moving forward or backward
        if (m_moveData.moveCmd.ForwardMove == 0 || wishspeed == 0)
            return;

        zspeed = m_moveData.V_PlayerVelocity.y;
        m_moveData.V_PlayerVelocity.y = 0;
        /* Next two lines are equivalent to idTech's VectorNormalize() */
        speed = m_moveData.V_PlayerVelocity.magnitude;
        m_moveData.V_PlayerVelocity.Normalize();

        dot = Vector3.Dot(m_moveData.V_PlayerVelocity, wishdir);
        k = 32;
        k *= m_moveData.P_AirControl * dot * dot * m_character.Runner.DeltaTime;// Time.deltaTime;

        // Change direction while slowing down
        if (dot > 0)
        {
            m_moveData.V_PlayerVelocity.x = m_moveData.V_PlayerVelocity.x * speed + wishdir.x * k;
            m_moveData.V_PlayerVelocity.y = m_moveData.V_PlayerVelocity.y * speed + wishdir.y * k;
            m_moveData.V_PlayerVelocity.z = m_moveData.V_PlayerVelocity.z * speed + wishdir.z * k;

            m_moveData.V_PlayerVelocity.Normalize();
            m_moveData.V_MoveDirectionNorm = m_moveData.V_PlayerVelocity;
        }

        m_moveData.V_PlayerVelocity.x *= speed;
        m_moveData.V_PlayerVelocity.y = zspeed; // Note this line
        m_moveData.V_PlayerVelocity.z *= speed;

    }


    #endregion Character Movement Functions




    public SlideOnObstacleData SlideOnObstacle(Ray ray, float rayDistance, Vector3 directionVector, float currentSpeed, Vector3 upVector)
    {
        m_directionVector = directionVector;
        directionRay = ray;
        hits = new RaycastHit[10];
        if (Physics.RaycastNonAlloc(directionRay, hits, rayDistance) > 0)//, playerCollisionLayers) > 0)
        {
            HitTransform = hits[0].transform;
            if (!HitTransform.CompareTag("Level"))
            {
                return new SlideOnObstacleData(m_directionVector, currentSpeed, false);
            }
            if (HitTransform.CompareTag("Level"))
            {
                CrossResult = Vector3.Cross(hits[0].normal, upVector);
                DotResult = Vector3.Dot(CrossResult, m_directionVector);
                if (DotResult < 0)
                {
                    CorrectedDirection = CrossResult * -1;
                }
                else
                {
                    CorrectedDirection = CrossResult;
                }

                //Do this if absolutely need to stop if corrected direction has an obstacle in its path
                //if (Physics.Raycast(transform.position, CorrectedDirection, rayDistance, playerCollisionLayers))
                //{
                //    speed = 0;
                //}

                //Debug.Log($"Hit Target: {HitTransform.tag}, {HitTransform.transform.name}");


                //We are not Modifying the Rotation here, rather the m_directionVector is used to Calculate Movement Direction. So player would move sideways along wall, if he ran into it.
                //Using state.DirectionVector here, will change and synchronize the values across the network. But we do NOT want it to change in this situation.
                m_directionVector = CorrectedDirection.normalized;

                float speedDotResult =  Vector3.Dot(transform.forward, hits[0].normal);
                currentSpeed = speedDotResult < 0 ? currentSpeed *= 1 + speedDotResult : currentSpeed;

                return new SlideOnObstacleData(m_directionVector, currentSpeed, true);
            }

            return new SlideOnObstacleData(m_directionVector, currentSpeed, false);
        }
        else
        {
            return new SlideOnObstacleData(m_directionVector, currentSpeed, false);
        }
    }

    private Transform groundTransform;
    private RaycastHit[] groundHits;
    public GroundCheckData GroundCheck(Ray ray)
    {
        groundHits = new RaycastHit[10];
        var origin = transform.position + new Vector3(0, transform.localScale.y / 2, 0);

        //Debug.DrawLine(origin, origin -transform.up * transform.localScale.y, Color.magenta, 5);
        if (Physics.RaycastNonAlloc(ray, groundHits, ray.direction.magnitude) > 0)
        {
            foreach (var groundHit in groundHits)
            {
                if (groundHit.transform == this.transform)
                {
                    continue;
                }
                //Debug.Log("Grounded");
                return new GroundCheckData(true, groundHit.point.y);
            }
        }
        return new GroundCheckData(false, transform.position.y);
    }

}

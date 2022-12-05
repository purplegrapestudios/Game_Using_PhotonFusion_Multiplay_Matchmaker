using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterShootComponent : NetworkBehaviour
{
    public LayerMask m_damagableLayerMask = ~(1 << 9);  //Do not hit itemLayer
    [SerializeField] private int m_fireRate = 10;
    [SerializeField] private Character m_character;
    [SerializeField] private CharacterCamera m_characterCamera;
    [SerializeField] private CharacterMuzzleComponent m_characterMuzzle;
    [SerializeField] private ParticleSystem m_muzzleFlash;
    [SerializeField] private bool m_isInitialized;
    private System.Action<float> m_takeDamageCallback;
    [Networked] public bool NetworkedFire { get; set; }

    private InputData m_inputData;
    private App m_app;

    public void Initialize(Character character, CharacterCamera characterCamera, CharacterMuzzleComponent characterMuzzle, ParticleSystem MuzzleFlash, System.Action<float> damageCallback)
    {
        m_app = App.FindInstance();
        m_character = character;
        m_muzzleFlash = MuzzleFlash;
        m_characterCamera = characterCamera;
        m_characterMuzzle = characterMuzzle;
        m_characterMuzzle.Initialize(this, m_muzzleFlash);

        NetworkedFire = false;
        m_takeDamageCallback = damageCallback;
        m_isInitialized = true;
    }

    public override void FixedUpdateNetwork()
    {
        if (!m_isInitialized) return;
        
        if (m_character.PlayerInputEnabled() && GetInput(out InputData data))
        {
            m_inputData = data;
            if (data.GetButton(ButtonFlag.FIRE))
            {
                if (FireCoroutine != null)
                    return;

                NetworkedFire = true;
            }
            FireInput(m_inputData);
        }
    }

    private void FireInput(InputData data)
    {
        if (NetworkedFire)
        {
            if (FireCoroutine != null) return;
            BeginFireCoroutine();
        }
    }

    private void BeginFireCoroutine()
    {
        if (FireCoroutine != null)
        {
            Debug.Log("Still Firing");
            return; 
        }
        FireCoroutine = FireCO();
        StartCoroutine(FireCoroutine);
    }

    IEnumerator FireCoroutine;
    IEnumerator FireCO()
    {
        if (NetworkedFire)
        {
            FireHitScanWeapon();
            yield return new WaitForSeconds(1f / m_fireRate);
        }
        StopFireCoroutine();
    }

    private void StopFireCoroutine()
    {
        NetworkedFire = false;
        StopCoroutine(FireCoroutine);
        FireCoroutine = null;
    }

    private void SpawnProjectile()
    {
        ObjectPoolManager.Instance.SpawnProjectile(m_muzzleFlash.transform.position + m_characterCamera.transform.forward, transform.position + m_characterCamera.transform.forward * 100, HitTargets.Player, Runner.LocalPlayer, m_muzzleFlash.transform);
    }

    private void FireHitScanWeapon()
    {
        if (!NetworkedFire) return;

        var rot = m_character.GetComponent<NetworkRigidbody>().ReadRotation() * Quaternion.AngleAxis(m_characterCamera.NetworkedRotationY, Vector3.left);
        var dir = rot * Vector3.forward;
        var orig = m_characterCamera.NetworkedPosition;
        Runner.LagCompensation.Raycast(origin: orig, direction: dir, 100, player: Object.InputAuthority, hit: out var hitInfo, layerMask: m_damagableLayerMask, HitOptions.IncludePhysX);
        //Debug.DrawRay(orig, dir * 100, Color.red, 0.1f);

        float hitDistance = 100;
        if (hitInfo.Distance > 0)
            hitDistance = hitInfo.Distance;
        
        if (hitInfo.Hitbox != null)
        {
            if (hitInfo.Hitbox.Root.GetComponent<Character>().Object.Id != m_character.Object.Id)
            {
        
                //Debug.Log($"We hit a HitBox Object: {hitInfo.Hitbox.transform.root.name}, Pos: {hitInfo.Point}");
                ObjectPoolManager.Instance.SpawnImpact(hitInfo.Point, hitInfo.Normal, HitTargets.Player);

                //if ((m_app.IsServerMode() && HasStateAuthority) || (m_app.IsHostMode()))
                if (hitInfo.Hitbox.Root.GetComponent<Character>().HasStateAuthority)
                {
                    Debug.Log($"State Authority took {5} damage");
                    hitInfo.Hitbox.Root.GetComponent<CharacterShootComponent>().m_takeDamageCallback(5);
                }
                else if (HasStateAuthority)
                {
                    Debug.Log($"{m_character.Player.Name} took {5} damage");
                    hitInfo.Hitbox.Root.GetComponent<CharacterShootComponent>().m_takeDamageCallback(5);
                }
            }
        }
        else if (hitInfo.Collider != null)
        {
            //Debug.Log($"We hit a Physx Object: {hitInfo.Collider.transform.name}, Pos: {hitInfo.Point}");
            ObjectPoolManager.Instance.SpawnImpact(hitInfo.Point, hitInfo.Normal, HitTargets.Environment);
        }
    }

}
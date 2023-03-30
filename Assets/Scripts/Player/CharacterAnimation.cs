﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public enum PlayerCameraView
{
    FirstPerson,
    ThirdPerson,
}

public class CharacterAnimation : NetworkBehaviour
{
    /// <summary>
    /// VARIABLE SECTION
    /// </summary>
    //Player components required in animating the player
    private CharacterMoveComponent m_characterMoveComponent;
    private CharacterShootComponent m_characterShootComponent;
    private CharacterHealthComponent m_characterHealthComponent;
    //private PlayerShooting PlayerShooting;
    private CharacterCamera m_characterCamera;
    private Animator Animator_1stPerson;
    private Animator Animator_3rdPerson;
    private Camera PlayerCamera;
    private Camera DeathCamera;

    //Animator Component: Parameters (Parameters Hashed. We update these values for actual animation to happen)
    private int Param_3rdPersonLowerBody;
    private int Param_3rdPersonUpperBody;
    private int Param_3rdPerson_AimAngle;
    private int Param_CrouchInt;
    private int Param_JumpInt;
    private int Param_DeadInt;
    private int Param_Speed;
    private int Param_AimInt;
    private int Param_FireInt;
    private int Param_ReloadInt;
    private int Param_SwitchWeaponInt;
    private int Param_1stPersonUpperBody_AR;

    //Networked PlayerAnimator Component: Parameter values
    private int Anim_INT_AssaultRifle;           //For TPS View Only (Set by !Photonview.isMine)
    private int Anim_INT_AssaultRifle_Current;   //For TPS View Only (Set by !Photonview.isMine)
    private int Anim_INT_LegRunning;             //For TPS View Only (Set by !Photonview.isMine)
    private bool Anim_BOOL_Jump;                 //For TPS View Only (Set by !Photonview.isMine)
    private bool Anim_BOOL_Death;                //For TPS View Only (Set by !Photonview.isMine)

    //Local PlayerAnimator Component: Parameter values
    private int AnimLocal_INT_UpperBody;    //For TPS View (Set by PhtonView.isMine)
    private int AnimLocal_INT_LowerBody;    //For TPS View (Set by PhtonView.isMine)
    private bool AnimLocal_BOOL_Jump;       //For TPS View (Set by PhtonView.isMine)
    private bool AnimLocal_BOOL_Death;      //For TPS View (Set by PhtonView.isMine)
    private int AnimLocal_INT_Arms;         //For FPS view (Set by PhtonView.isMine)

    //Variables to control animation priorities
    private bool armPriorityAnimation;
    private bool shotPriorityAnimation;

    //Player camer view variables (This is for switching between First and Third person views)
    public PlayerCameraView playerCameraView;
    private PlayerCameraView originalPlayerCamerView;
    private bool isInitialized;

    public void Initialize () {

        m_characterMoveComponent = GetComponent<CharacterMoveComponent>();
        m_characterShootComponent = GetComponent<CharacterShootComponent>();
        m_characterHealthComponent = GetComponent<CharacterHealthComponent>();
        m_characterCamera = GetComponent<CharacterComponents>().PlayerCamera.GetComponent<CharacterCamera>();
        Animator_1stPerson = GetComponent<CharacterComponents>().animator1;
        Animator_3rdPerson = GetComponent<CharacterComponents>().animator3;

        Param_3rdPersonLowerBody = Animator.StringToHash("Param_3rdPersonLowerBody");
        Param_3rdPersonUpperBody = Animator.StringToHash("Param_3rdPersonUpperBody");
        Param_3rdPerson_AimAngle = Animator.StringToHash("Param_3rdPerson_AimAngle");
        Param_CrouchInt = Animator.StringToHash("Param_CrouchInt");
        Param_JumpInt = Animator.StringToHash("Param_JumpInt");
        Param_DeadInt = Animator.StringToHash("Param_DeadInt");
        Param_Speed = Animator.StringToHash("Param_Speed");
        Param_AimInt = Animator.StringToHash("Param_AimInt");
        Param_FireInt = Animator.StringToHash("Param_FireInt");
        Param_ReloadInt = Animator.StringToHash("Param_ReloadInt");
        Param_SwitchWeaponInt = Animator.StringToHash("Param_SwitchWeaponInt");
        Param_1stPersonUpperBody_AR = Animator.StringToHash("Param_1stPersonUpperBody_AR");
        isInitialized = true;
    }


    bool __isFiringBullet;
    bool __isAiming;    

    private void LateUpdate()
    {
        if (!isInitialized) return;

        AnimationBehavior_OurPlayer(isPlayerAlive: true);
        __isFiringBullet = false;
        __isAiming = false;
    }

    private void AnimationBehavior_OurPlayer(bool isPlayerAlive)
    {
        //PLAYER IS ALIVE
        if (isPlayerAlive)
        {
            SetStateFloat(ref Param_Speed, m_characterMoveComponent.NetworkedVelocity.magnitude);
            SetStateFloat(ref Param_3rdPerson_AimAngle, m_characterCamera.NetworkedRotationY / 90f, smooth: .9f);
            SetStateInt(ref Param_FireInt, m_characterShootComponent.NetworkedFire ? 1 : 0);
            SetStateInt(ref Param_ReloadInt, m_characterShootComponent.NetworkedReload ? 1 : 0);
            SetStateInt(ref Param_SwitchWeaponInt, m_characterShootComponent.NetworkedSwitchWeapon ? 1 : 0);
            SetStateInt(ref Param_CrouchInt, m_characterMoveComponent.NetworkedIsCrouched ? 1 : 0);
            SetStateInt(ref Param_JumpInt, !m_characterMoveComponent.NetworkedFloorDetected ? 1 : 0);
            SetStateInt(ref Param_DeadInt, m_characterHealthComponent.NetworkedRespawn ? 1 : 0);

        }
    }


    /// <summary>
    /// Set Animator Component Parameters
    /// </summary>
    private void SetStateInt_3rdPersonLowerBody(int val)
    {
        if ((playerCameraView.Equals(PlayerCameraView.ThirdPerson)) && Animator_3rdPerson.gameObject.activeSelf)
            Animator_3rdPerson.SetFloat(Param_3rdPersonLowerBody, val);
    }

    private void SetStateInt_3rdPersonUpperBody(int val)
    {
        if ((playerCameraView.Equals(PlayerCameraView.ThirdPerson)) && Animator_3rdPerson.gameObject.activeSelf)
            Animator_3rdPerson.SetInteger(Param_3rdPersonUpperBody, val);
    }

    private void SetStateBool_3rdPersonJump(bool val)
    {
        if ((playerCameraView.Equals(PlayerCameraView.ThirdPerson)) && Animator_3rdPerson.gameObject.activeSelf)
            Animator_3rdPerson.SetBool(Param_JumpInt, val);
    }

    private void SetStateBool_3rdPersonDeath(bool val)
    {
        if ((playerCameraView.Equals(PlayerCameraView.ThirdPerson)) && Animator_3rdPerson.gameObject.activeSelf)
            Animator_3rdPerson.SetBool(Param_DeadInt, val);
    }

    private void SetStateFloat(ref int param, float val, float smooth = 1)
    {
        if ((playerCameraView.Equals(PlayerCameraView.ThirdPerson)) && Animator_3rdPerson.gameObject.activeSelf)

            if (smooth == 1)
            {
                Animator_3rdPerson.SetFloat(param, val);
                return;
            }

        var value = Animator_3rdPerson.GetFloat(param);
        var lerpedValue = Mathf.Lerp(value, val, smooth);
        Animator_3rdPerson.SetFloat(param, lerpedValue);
    }

    private void SetStateBool(ref int param, bool val)
    {
        if ((playerCameraView.Equals(PlayerCameraView.ThirdPerson)) && Animator_3rdPerson.gameObject.activeSelf)
            Animator_3rdPerson.SetBool(param, val);
    }

    private void SetStateInt(ref int param, int val)
    {
        if ((playerCameraView.Equals(PlayerCameraView.ThirdPerson)) && Animator_3rdPerson.gameObject.activeSelf)
            Animator_3rdPerson.SetInteger(param, val);
    }


    private void SetStateFloat_3rdPersonAimAngle(float val, float smooth = 1f)
    {
        if ((playerCameraView.Equals(PlayerCameraView.ThirdPerson)) && Animator_3rdPerson.gameObject.activeSelf)
        {
            if (smooth == 1)
            {
                Animator_3rdPerson.SetFloat(Param_3rdPerson_AimAngle, val);
                return;
            }

            var curAimAngle = Animator_3rdPerson.GetFloat(Param_3rdPerson_AimAngle);
            var lerpedAimAngle = Mathf.Lerp(curAimAngle, val, smooth);
            Animator_3rdPerson.SetFloat(Param_3rdPerson_AimAngle, lerpedAimAngle);
        }
    }

    private void SetStateInt_1stPersonArms(int val)
    {
        return;
        if (playerCameraView.Equals(PlayerCameraView.FirstPerson))
            Animator_1stPerson.SetInteger(Param_1stPersonUpperBody_AR, val);
    }

    private int GetStateInt(ref int param)
    {
        if ((playerCameraView.Equals(PlayerCameraView.ThirdPerson)) && Animator_3rdPerson.gameObject.activeSelf)
            return Animator_3rdPerson.GetInteger(param);

        return -1;
    }

    /// <summary>
    /// UPDATE: Switch camera view behavior
    /// </summary>
    private void SwitchCameraBehavior()
    {
        //if (InputManager.Instance.GetKeyDown(InputCode.SwitchPerspective))
        //{
        //    if (EventManager.Instance.GetScore(PhotonView.owner.NickName, PlayerStatCodes.Health) > 0)
        //    {
        //        if (playerCameraView.Equals(PlayerCameraView.FirstPerson))
        //        {
        //            SwitchCameraPerspective(PlayerCameraView.ThirdPerson);
        //        }
        //        else if (playerCameraView.Equals(PlayerCameraView.ThirdPerson))
        //        {
        //            SwitchCameraPerspective(PlayerCameraView.FirstPerson);
        //        }
        //    }
        //}
    }

    /// <summary>
    /// Switch between First Person and Third Person camera view
    /// </summary>
    private void SwitchCameraPerspective(PlayerCameraView view)
    {
        //GetComponent<PlayerObjectComponents>().PlayerCamera.GetComponent<Camera>().cullingMask ^= 1 << LayerMask.NameToLayer("Player");
        AnimLocal_INT_UpperBody = -1;
        AnimLocal_INT_LowerBody = -1;
        AnimLocal_BOOL_Jump = !AnimLocal_BOOL_Jump;
        AnimLocal_INT_Arms = -1;

        if (view.Equals(PlayerCameraView.FirstPerson))
        {
            GetComponent<CharacterComponents>().Dolly.GetComponent<CharacterCameraDolly>().RestDollyPosition();
            GetComponent<CharacterComponents>().PlayerCamera.GetComponent<Camera>().cullingMask &= ~(1 << LayerMask.NameToLayer("Player"));
            playerCameraView = PlayerCameraView.FirstPerson;
            GetComponent<CharacterComponents>().ThirdPersonPlayer.SetActive(false);
            GetComponent<CharacterComponents>().FirstPersonPlayer.SetActive(true);
        }
        else if (view.Equals(PlayerCameraView.ThirdPerson))
        {
            GetComponent<CharacterComponents>().Dolly.GetComponent<CharacterCameraDolly>().RestDollyPosition();
            GetComponent<CharacterComponents>().PlayerCamera.GetComponent<Camera>().cullingMask |= 1 << LayerMask.NameToLayer("Player");
            playerCameraView = PlayerCameraView.ThirdPerson;
            GetComponent<CharacterComponents>().ThirdPersonPlayer.SetActive(true);
            GetComponent<CharacterComponents>().FirstPersonPlayer.SetActive(false);

        }
    }

}
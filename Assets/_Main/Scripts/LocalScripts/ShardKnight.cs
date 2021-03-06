﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Gamekit2D;
using BTAI;

public class ShardKnight : MonoBehaviour {

    public Transform targetToTrack;

    [Header("Laser")]
    public Transform laserStartingPosition;
    public Transform laserFollower;
    public Gradient laserFollowAttackGradient;
    public Gradient circleLaserAttackGradient;
    public GameObject laserAttackBullet;


    [Header("Exploding Attacks")]
    public GameObject concentratingAttack;

    [Header("Dash")]
    public GameObject dashEffect;
    public float dashSpeed = 30f;

    [Header("Move Positions")]
    public List<Transform> movePositions;


    [Header("Effect")]
    public string deathEffectName;
    public string hitEffectName;

    [Header("Audio")]
    public RandomAudioPlayer hitAudio;
    public RandomAudioPlayer concentratingAudio;

    [Header("Others")]
    public float timeBetweenFlickering = 0.1f;
    public ParticleSystem concentratingStateEffect;
    public ParticleSystem tornadoEffect;


    private Animator m_Animator;
    private Rigidbody2D m_RigidBody2D;
    private SpriteRenderer m_SpriteRenderer;
    private LineRenderer m_LineRenderer;
    private TrailRenderer m_trailRenderer;

    private BulletPool m_ConcentratingAttackPool;
    private BulletPool m_LaserAttackPool;



    private int m_HashMeteorShowerAttackPara = Animator.StringToHash("MeteorShowerAttack");
    private int m_HashExplodingAttackPara = Animator.StringToHash("ExplodingAttack");
    private int m_HashHurtPara = Animator.StringToHash("Hurt");
    private int m_HashTransformPara = Animator.StringToHash("Transform");
    private int m_HashEndAnimationPara = Animator.StringToHash("End");
    private Flicker m_Flicker;
    private Damageable m_Damageable;

    private int m_DeathEffectHash;
    private int m_HitEffectHash;

    //others
    private Damager m_CurrentConcentratingAttack;
    private bool m_FormChanged = false;
    private Vector3 m_FuturePosition;
    private int m_CurrentMoveIndex;
    private Vector3 m_OffsetFromLaserToShardKnight;
    private Vector3 m_OriginalPosition;
    private bool m_StartAction = false;

    //Laser
    private bool m_LaserEnabled = false;
    private float m_LaserAttackShotAngle = 0;
    private float m_LaserSweptAngle;
    private float laserAttackType; //1 is laserFollowAttack, 2 and 3 is circleLaseAttack
    private FollowTarget m_LaserFollowComponent;


    //Behavior Tree
    private Root m_Ai = BT.Root();

    private void Awake()
    {
        m_Animator = GetComponentInChildren<Animator>();
        m_RigidBody2D = GetComponent<Rigidbody2D>();
        m_Damageable = GetComponent<Damageable>();
        m_SpriteRenderer = GetComponentInChildren<SpriteRenderer>();
        m_LineRenderer = GetComponentInChildren<LineRenderer>();
        m_Flicker = m_Animator.gameObject.AddComponent<Flicker>();
        m_LaserFollowComponent = laserFollower.GetComponent<FollowTarget>();
        m_trailRenderer = GetComponentInChildren<TrailRenderer>();

        m_OriginalPosition = transform.position;

        m_DeathEffectHash = VFXController.StringToHash(deathEffectName);
        m_HitEffectHash = VFXController.StringToHash(hitEffectName);

        //Pool
        m_ConcentratingAttackPool = BulletPool.GetObjectPool(concentratingAttack, 5);
        m_LaserAttackPool = BulletPool.GetObjectPool(laserAttackBullet, 20);


        m_OffsetFromLaserToShardKnight = laserStartingPosition.position - m_SpriteRenderer.transform.position;

        //Behaviour tree
        m_Ai.OpenBranch(
            BT.If(() => { return m_Damageable.CurrentHealth >= m_Damageable.startingHealth / 2; }).OpenBranch(

                 BT.RandomSequence(new int[] { 3, 2, 4 }, 2).OpenBranch(
                    //Laser follow attack
                    BT.Sequence().OpenBranch(
                        BT.Call(() => laserAttackType = 1),
                        BT.Call(() => m_LineRenderer.colorGradient = laserFollowAttackGradient),
                        BT.Call(StartLaserAttack),
                        BT.Wait(1.5f),
                        BT.Call(() => m_LaserFollowComponent.speed = 1f),
                        BT.Call(() => m_LineRenderer.enabled = false),
                        BT.Repeat(15).OpenBranch(
                            BT.Call(LaserFollowAttack),
                            BT.Wait(0.1f)
                        ),
                        BT.Call(() => m_LaserFollowComponent.speed = 2f),
                        BT.Call(EndLaserAttack),
                        BT.Wait(1f)
                    ),
                    //Cicle laser Attack
                    BT.Sequence().OpenBranch(
                        BT.Call(() => laserAttackType = Random.Range(2, 4)),
                        BT.Call(() => m_LineRenderer.colorGradient = circleLaserAttackGradient),
                        BT.Call(StartLaserAttack),
                        BT.WaitUntil(() => Mathf.Abs(m_LaserSweptAngle) >= 360),
                        BT.Call(() => m_LineRenderer.enabled = false),
                        BT.Repeat(19).OpenBranch(
                            BT.Call(CircleLaserAttack),
                            BT.Wait(0.1f)
                        ),
                        BT.Call(EndLaserAttack),
                        BT.Wait(1f)
                    ),
                    //Dash
                    BT.Sequence().OpenBranch(
                        BT.Call(()=> tornadoEffect.Play()),
                        BT.Wait(1.5f),
                        BT.Call(DashToLower, 3f, false),
                        BT.WaitUntil(MoveCheck),
                        BT.Wait(0.5f),
                        BT.Call(StartDashing),
                        BT.Call(DashToLower, 10f, true),
                        BT.Call(RotateTowardsFuturePosition),
                        BT.WaitUntil(MoveCheck),
                        BT.Call(EndDashing),
                        BT.Wait(0.5f),
                        BT.Call(FlipSpriteBasedOnSide),
                        BT.Call(DashToUpper, 3f, false),
                        BT.WaitUntil(MoveCheck),
                        BT.Call(() => tornadoEffect.Stop()),
                        BT.Wait(1.5f)
                    )

                )
            ),

            BT.If(() => { return m_Damageable.CurrentHealth < m_Damageable.startingHealth / 2; }).OpenBranch(
                //Change form animation
                BT.If(() => { return !m_FormChanged; }).OpenBranch(
                    BT.Call(ChangeForm),
                    BT.WaitForAnimatorState(m_Animator, "ShardKnightAfterTransform"),
                    BT.Wait(1f)
                 ),

                BT.RandomSequence(new int[] { 3, 3, 2, 2 }, 2).OpenBranch(
                    //Dash
                    BT.Sequence().OpenBranch(
                        BT.Call(() => tornadoEffect.Play()),
                        BT.Wait(1.5f),
                        BT.Call(StartDashing),
                        BT.Call(DashToLower, 10f, true),
                        BT.Call(RotateTowardsFuturePosition),
                        BT.WaitUntil(MoveCheck),
                        BT.Wait(0.2f),
                        BT.Call(FlipSpriteBasedOnSide),
                        BT.Call(DashToLower, 12f, true),
                        BT.Call(RotateTowardsFuturePosition),
                        BT.WaitUntil(MoveCheck),
                        BT.Wait(0.2f),
                        BT.Call(FlipSpriteBasedOnSide),
                        BT.Call(DashToUpper, 10f, true),
                        BT.Call(RotateTowardsFuturePosition),
                        BT.WaitUntil(MoveCheck),
                        BT.Call(EndDashing),
                        BT.Call(FlipSpriteBasedOnSide),
                        BT.Call(() => tornadoEffect.Stop()),
                        BT.Wait(1.5f)

                    ),
                    //Exploding Attack
                    BT.Sequence().OpenBranch(
                        BT.Call(() => m_Animator.SetTrigger(m_HashExplodingAttackPara)),
                        BT.Repeat(5).OpenBranch(
                            BT.Call(SpawnConcentratingAttack),
                            BT.Wait(0.5f),
                            BT.Call(EnableConcentratingAttackDamager),
                            BT.Wait(0.8f)
                        ),
                        BT.Call(() => m_Animator.SetTrigger(m_HashEndAnimationPara)),
                        BT.Wait(2f)
                    ),
                    //Cicle laser Attack
                    BT.Sequence().OpenBranch(
                        BT.Call(() => laserAttackType = Random.Range(2, 4)),
                        BT.Call(() => m_LineRenderer.colorGradient = circleLaserAttackGradient),
                        BT.Call(StartLaserAttack),
                        BT.WaitUntil(() => Mathf.Abs(m_LaserSweptAngle) >= 360),
                        BT.Call(() => m_LineRenderer.enabled = false),
                        BT.Repeat(19).OpenBranch(
                            BT.Call(CircleLaserAttack),
                            BT.Wait(0.1f)
                        ),
                        BT.Call(EndLaserAttack),
                        BT.Wait(1f)
                    ),
                    //Meteor shower
                    BT.Sequence().OpenBranch(
                        BT.Call(() => m_Animator.SetTrigger(m_HashMeteorShowerAttackPara)),
                        BT.Wait(0.5f),
                        BT.Call(() => concentratingStateEffect.Play()),
                        BT.Wait(1f),
                        BT.Repeat(30).OpenBranch(
                            BT.Call(MeteorShowerAttack),
                            BT.Wait(0.2f)
                        ),
                        BT.Call(() => m_Animator.SetTrigger(m_HashEndAnimationPara)),
                        BT.Call(() => concentratingStateEffect.Stop()),
                        BT.Wait(1f)
                    )
                 ),

                BT.Call(OrientToTarget)
              )

            
        );

    }


    public Root GetAIRoot()
    {
        return m_Ai;
    }

    public void StartAction()
    {
        m_StartAction = true;
    }
 

    // Update is called once per frame
    void Update () {

        if (m_StartAction)
        {
            m_Ai.Tick();
        }

        if(m_LaserEnabled)
        {
            if (laserAttackType == 1)
            {
                TrackTargerByLaser();
            }
            else
            {
                DrawLaserFullCircle();
            }
        }
       

    }


    private void OrientToTarget()
    {
        if (targetToTrack == null) return;

        if (targetToTrack.position.x > transform.position.x)
        {
            m_SpriteRenderer.flipX = true;
        }
        else
        {
            m_SpriteRenderer.flipX = false;
        }

    }


    private void SpawnConcentratingAttack()
    {

        Vector3 spawnPosition = new Vector3(targetToTrack.transform.position.x, targetToTrack.transform.position.y + 0.5f, 0);

        BulletObject concentratingAttack = m_ConcentratingAttackPool.Pop(spawnPosition);

        m_CurrentConcentratingAttack = concentratingAttack.bullet.GetComponent<Damager>();

        m_CurrentConcentratingAttack.DisableDamage();
    }

    private void EnableConcentratingAttackDamager()
    {
        CameraShaker.Shake(0.05f, 0.1f);

        m_CurrentConcentratingAttack.EnableDamage();
    }




    private void ChangeForm()
    {
        m_Animator.SetTrigger(m_HashTransformPara);
        m_FormChanged = true;
    }
    


    private bool MoveCheck()
    {
        if ((transform.position - m_FuturePosition).sqrMagnitude <= 0.5f)
        {
            m_RigidBody2D.velocity = Vector2.zero;
            return true;
        }

        return false;
    }

    private void MoveTo(Vector3 position, float speed)
    {
        Vector3 direction = (position - transform.position).normalized;

        m_RigidBody2D.velocity = direction * speed;

        //transform.position = Vector3.Slerp(transform.position, position, speed);

    }

    private void DashToLower(float speed, bool switchSide = true)
    {
        if (m_CurrentMoveIndex == 2 || m_CurrentMoveIndex == 0)
        {
            if (switchSide)
            {
                MoveTo(movePositions[1].position, speed);
                m_FuturePosition = movePositions[1].position;
                m_CurrentMoveIndex = 1;
            }
            else
            {
                MoveTo(movePositions[0].position, speed);
                m_FuturePosition = movePositions[0].position;
                m_CurrentMoveIndex = 0;
            }
        }
        else
        {
            if (switchSide)
            {
                MoveTo(movePositions[0].position, speed);
                m_FuturePosition = movePositions[0].position;
                m_CurrentMoveIndex = 0;
            }
            else
            {
                MoveTo(movePositions[1].position, speed);
                m_FuturePosition = movePositions[1].position;
                m_CurrentMoveIndex = 1;
            }

        }
    }

    private void DashToUpper(float speed, bool switchSide = true)
    {
        Vector3 futurePosition;

        if (m_CurrentMoveIndex == 2 || m_CurrentMoveIndex == 0)
        {
            if (switchSide)
            {
                futurePosition = movePositions[3].position;
                futurePosition.x = Random.Range(movePositions[3].position.x, movePositions[4].position.x);
                m_CurrentMoveIndex = 3;
            }
            else
            {
                futurePosition = movePositions[2].position;
                futurePosition.x = Random.Range(movePositions[2].position.x, movePositions[4].position.x);
                m_CurrentMoveIndex = 2;
            }
        }
        else
        {
            if (switchSide)
            {
                futurePosition = movePositions[2].position;
                futurePosition.x = Random.Range(movePositions[2].position.x, movePositions[4].position.x);
                m_CurrentMoveIndex = 2;
            }
            else
            {
                futurePosition = movePositions[3].position;
                futurePosition.x = Random.Range(movePositions[3].position.x, movePositions[4].position.x);
                m_CurrentMoveIndex = 3;
            }

        }

        MoveTo(futurePosition, speed);
        m_FuturePosition = futurePosition;
    }

    private void StartDashing()
    { 
        dashEffect.SetActive(true);
        m_trailRenderer.enabled = true;
    }

    private void EndDashing()
    {  
        dashEffect.SetActive(false);
        m_trailRenderer.enabled = false;
        transform.rotation = Quaternion.identity;
        m_RigidBody2D.velocity = Vector2.zero;
    }

    private void RotateTowardsFuturePosition()
    {
        Vector3 direction = (m_FuturePosition - transform.position).normalized;

        //rotate shardknight
        float rotationZ = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, rotationZ - 90);
    }

    private void FlipSpriteBasedOnSide()
    {
        if (m_CurrentMoveIndex == 2 || m_CurrentMoveIndex == 0)
        {
            m_SpriteRenderer.flipX = false;
        }
        else
        {
            m_SpriteRenderer.flipX = true;
        }
    }




    private void StartLaserAttack()
    {
        laserFollower.gameObject.SetActive(true);
        m_LineRenderer.enabled = true;
        m_LaserEnabled = true;
        m_LaserAttackShotAngle = 0;
        m_LaserSweptAngle = 0;

        laserStartingPosition.ChangeOffsetBasedOnSpriteFacing(m_SpriteRenderer.transform, m_SpriteRenderer, m_OffsetFromLaserToShardKnight);

    }

    private void EndLaserAttack()
    {
        laserFollower.gameObject.SetActive(false);
        m_LineRenderer.enabled = false;
        m_LaserEnabled = false;
        m_LineRenderer.startWidth = 0.05f;
        m_LineRenderer.endWidth = 0.05f;
    }
    
    private void TrackTargerByLaser()
    {
        m_LineRenderer.SetPosition(0, laserStartingPosition.position);

        Vector3 direction = laserFollower.position - laserStartingPosition.position;

        //if the laser hit something
        RaycastHit2D m_RaycastHit2D = Physics2D.Raycast(laserStartingPosition.position, direction, 100, LayerMask.GetMask("Player","Platform"));

        if (m_RaycastHit2D)
        {
            m_LineRenderer.SetPosition(1, m_RaycastHit2D.point);
        }
        else
        {
            m_LineRenderer.SetPosition(1, laserStartingPosition.position + direction * 100);

        }


        m_LineRenderer.startWidth += 0.06f * Time.deltaTime;
        m_LineRenderer.endWidth += 0.06f * Time.deltaTime;
    }

    private void DrawLaserFullCircle()
    {
        m_LineRenderer.SetPosition(0, laserStartingPosition.position);

        Vector3 direction = Quaternion.Euler(0, 0, m_LaserSweptAngle) * Vector2.down;

        //if the laser hit something
        RaycastHit2D m_RaycastHit2D = Physics2D.Raycast(laserStartingPosition.position, direction, 100, LayerMask.GetMask("Player", "Platform"));

        if (m_RaycastHit2D)
        {
            m_LineRenderer.SetPosition(1, m_RaycastHit2D.point);
        }
        else
        {
            m_LineRenderer.SetPosition(1, laserStartingPosition.position + direction * 100);

        }

        if (laserAttackType == 2)
        {
            m_LaserSweptAngle += 200 * Time.deltaTime;
        }
        else
        {
            m_LaserSweptAngle -= 200 * Time.deltaTime;
        }

    }

    private void LaserFollowAttack()
    {
        Vector2 startingShootPosition1 = new Vector3(-0.45f, 0.45f,0) + transform.position;
        Vector2 startingShootPosition2 = new Vector3(0.45f, 0.45f,0) + transform.position;
        
        //get bullet object
        BulletObject laserAttackBulletObject = m_LaserAttackPool.Pop(Random.Range(0, 2) == 1 ? startingShootPosition1 : startingShootPosition2);
        
        Vector2 direction = (laserFollower.position - laserAttackBulletObject.transform.position).normalized;

        //rotate
        laserAttackBulletObject.transform.RotateToDirection(direction);

        laserAttackBulletObject.rigidbody2D.velocity = direction * 15f;
    }

    private void CircleLaserAttack()
    {
        Vector2 startingShootPosition = transform.position;

        //get bullet object
        BulletObject laserAttackBulletObject = m_LaserAttackPool.Pop(startingShootPosition);

        Vector2 direction = (Quaternion.Euler(0,0, m_LaserAttackShotAngle) * Vector2.down).normalized;

        //rotate
        laserAttackBulletObject.transform.RotateToDirection(direction);

        laserAttackBulletObject.rigidbody2D.velocity = direction * 10f;

        if (laserAttackType == 2)
        {
            m_LaserAttackShotAngle += 20;
        }
        else
        {
            m_LaserAttackShotAngle -= 20;
        }
    }


    private void MeteorShowerAttack()
    {
        Vector2 startingShootPosition = new Vector2(targetToTrack.position.x + Random.Range(-3f,3f), targetToTrack.position.y + 10f);

        //get bullet object
        BulletObject laserAttackBulletObject = m_LaserAttackPool.Pop(startingShootPosition);

        //rotate
        laserAttackBulletObject.transform.RotateToDirection(Vector2.down);

        laserAttackBulletObject.rigidbody2D.velocity = Vector2.down * 8f;
    }


    public void GotHit(Damager damager, Damageable damageable)
    {
        m_Flicker.StartColorFickering(damageable.invulnerabilityDuration, timeBetweenFlickering);
        VFXController.Instance.Trigger(m_HitEffectHash, transform.position, 0, false, null);
        if (hitAudio != null)
            hitAudio.PlayRandomSound();
    }


    public void Die(Damager damager, Damageable damageable)
    {
        VFXController.Instance.Trigger(m_DeathEffectHash, transform.position, 0, false, null);
    }

}

﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class onHitSummoner : MonoBehaviour {
    public GameObject hitEffect;
    [Tooltip("Just testing")]
    public Transform particlePos;
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.tag =="Acid")
        {
            Debug.Log("hit: " );
            Instantiate(hitEffect, particlePos);
        }
    }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        
    }

}

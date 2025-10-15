using System;
using UnityEngine;

public class WindManager : MonoBehaviour
{
    WindZone windZone;
    [SerializeField] private float windForce;
    [SerializeField] private float windAngle;
    
    
    private void Start()
    {
        windZone = GetComponent<WindZone>();
    }

    private void Update()
    {
        windZone.windMain = windForce;
        transform.eulerAngles = new Vector3(0, windAngle, 0);
    }
}

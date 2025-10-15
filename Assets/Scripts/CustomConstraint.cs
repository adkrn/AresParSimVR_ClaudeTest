using System;
using UnityEngine;
using UnityEngine.Serialization;

public class CustomConstraint : MonoBehaviour
{
    [SerializeField] private Transform target;

    Vector3 _firstDistance;
    private Vector3 _thisRot;
    
    [SerializeField] private Vector3 _offsetRot = Vector3.zero;
    private void Start()
    {
        _firstDistance = transform.position - target.position;
        _thisRot = transform.eulerAngles; 
    }

    private void Update()
    {
        transform.position = target.position + _firstDistance;
        var targetRot = target.rotation.eulerAngles;
        transform.eulerAngles = new Vector3(_thisRot.x, targetRot.y, _thisRot.z) + _offsetRot;
    }
}

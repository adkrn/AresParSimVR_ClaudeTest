using System;
using UnityEngine;
using UnityEngine.Serialization;

public class CustomConstraint : MonoBehaviour
{
    [SerializeField] private Transform target;

    Vector3 _firstDistance;
    private Vector3 _thisRot;

    [SerializeField] private Transform pivotRot;
    [SerializeField] private string pivotRotTag = "PivotRot";

    [SerializeField] private Vector3 _offsetRot = Vector3.zero;

    private void OnEnable()
    {
        // pivotRot이 null이면 태그로 검색
        if (pivotRot == null)
        {
            GameObject pivotObj = GameObject.FindGameObjectWithTag(pivotRotTag);
            if (pivotObj != null)
            {
                pivotRot = pivotObj.transform;
                Debug.Log($"[CustomConstraint] PivotRot 자동 발견: {pivotObj.name}");
            }
            else
            {
                Debug.LogWarning($"[CustomConstraint] '{pivotRotTag}' 태그를 가진 오브젝트를 찾을 수 없습니다.");
            }
        }
    }

    private void Start()
    {
        _firstDistance = transform.position - target.position;
        _thisRot = transform.eulerAngles;
    }

    private void Update()
    {
        transform.position = target.position;
        transform.localRotation = pivotRot.localRotation;
    }
}

using UnityEngine;

public class CircularFlight : MonoBehaviour
{
    public Transform center;         // 회전 중심 (원 오브젝트)
    public float rotationSpeed = 30f; // 도/초

    void Update()
    {
        // 중심을 기준으로 회전
        transform.RotateAround(center.position, Vector3.up, rotationSpeed * Time.deltaTime);
        
        // 자식 오브젝트(비행기)가 항상 회전 방향을 향하게
        Transform plane = transform.GetChild(0);
        Vector3 dir = (center.position - plane.position).normalized;
        Vector3 forward = Vector3.Cross(Vector3.up, dir);
        plane.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
}

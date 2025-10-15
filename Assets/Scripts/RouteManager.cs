using System;
using System.Collections.Generic;
using UnityEngine;

public class RouteManager : MonoBehaviour
{
    [SerializeField] private GameObject routePrefab;
    [SerializeField] private Transform routeParent;

    public List<Route> routeList;
    public List<Transform> routePoints = new List<Transform>();

    private void Awake()
    {
        Init();
    }

    public void Init()
    {
        var routes = DataManager.Inst.routes;
        if (routes == null || routes.Count == 0)
        {
            Debug.LogWarning("[RouteManager] 사용할 수 있는 Route 데이터가 없습니다.");
            return;
        }

        // routeId로 정렬해서 순서대로 생성 (문자열 정렬)
        routes.Sort((a, b) => string.Compare(a.routeId, b.routeId, StringComparison.Ordinal));
        routeList = routes;
        
        foreach (var route in routes)
        {
            // 1. 프리팹 인스턴스 생성
            var go = Instantiate(routePrefab, routeParent);

            // 2. 위치 지정
            go.transform.position = new Vector3(route.pointX, 0f, route.pointZ);;

            // 3. 이름 설정
            go.name = $"Route_{route.routeId}";
            
            // 4. 순서대로 리스트에 추가
            routePoints.Add(go.transform);
        }

        Debug.Log($"[RouteManager] {routes.Count}개 Route 인스턴스 생성 완료");
        
        // AirPlane에게 route 포인트 전달
        var airplane = FindAnyObjectByType<AirPlane>();
        if (airplane != null)
        {
            airplane.SetRoutePoints(routePoints);
        }
    }
}

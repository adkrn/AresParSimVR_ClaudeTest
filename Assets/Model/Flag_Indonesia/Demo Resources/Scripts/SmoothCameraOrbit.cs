namespace UnluckSoftware
{
	using UnityEngine;
	[AddComponentMenu("Unluck Software/Smooth Mouse Orbit")]
	public class SmoothCameraOrbit : MonoBehaviour
	{
		public Transform target;
		public Vector3 targetOffset;
		public float distance = 5.0f;
		public float maxDistance = 20;
		public float minDistance = .6f;
		public float xSpeed = 200.0f;
		public float ySpeed = 200.0f;
		public int yMinLimit = -80;
		public int yMaxLimit = 80;
		public int zoomRate = 40;
		public float panSpeed = 0.3f;
		public float zoomDampening = 5.0f;
		public float autoRotate = 1;
		private float xDegree = 0.0f;
		private float yDegree = 0.0f;
		private float currentDistance;
		private float desiredDistance;
		private Quaternion currentRotation;
		private Quaternion targetRotation;
		private Quaternion rotation;
		private Vector3 position;
		private float idleTimer = 0.0f;
		private float idleSmooth = 0.0f;

		public void Start() {
			if (!target) {
				GameObject go = new GameObject("Cam Target");
				go.transform.position = transform.position + (transform.forward * distance);
				target = go.transform;
			}
			distance = Vector3.Distance(transform.position, target.position);
			currentDistance = distance;
			desiredDistance = distance;
			position = transform.position;
			rotation = transform.rotation;
			currentRotation = transform.rotation;
			targetRotation = transform.rotation;
			xDegree = Vector3.Angle(Vector3.right, transform.right);
			yDegree = Vector3.Angle(Vector3.up, transform.up);
			position = target.position - (rotation * Vector3.forward * currentDistance + targetOffset);
		}

		private void OnDrawGizmos()
		{
			if (!target) return;
			Gizmos.color = Color.grey;
			Gizmos.DrawWireSphere(target.position, .5f);
			Gizmos.DrawLine(transform.position, target.position);
		}

		void LateUpdate() {
			if (Input.GetMouseButton(2) && Input.GetKey(KeyCode.LeftAlt) && Input.GetKey(KeyCode.LeftControl)) {
				desiredDistance -= Input.GetAxis("Mouse Y") * 0.02f * zoomRate * 0.125f * Mathf.Abs(desiredDistance);
			}
			else if (Input.GetMouseButton(0)) {
				xDegree += Input.GetAxis("Mouse X") * xSpeed * 0.02f;
				yDegree -= Input.GetAxis("Mouse Y") * ySpeed * 0.02f;
				yDegree = ClampAngle(yDegree, yMinLimit, yMaxLimit);
				targetRotation = Quaternion.Euler(yDegree, xDegree, 0);
				currentRotation = transform.rotation;
				rotation = Quaternion.Lerp(currentRotation, targetRotation, 0.02f * zoomDampening);
				transform.rotation = rotation;
				idleTimer = 0;
				idleSmooth = 0;

			} else {
				idleTimer += 0.02f;
				if (idleTimer > autoRotate && autoRotate > 0) {
					idleSmooth += (0.02f + idleSmooth) * 0.005f;
					idleSmooth = Mathf.Clamp(idleSmooth, 0, 1);
					xDegree += xSpeed * 0.001f * idleSmooth;
				}
				yDegree = ClampAngle(yDegree, yMinLimit, yMaxLimit);
				targetRotation = Quaternion.Euler(yDegree, xDegree, 0);
				currentRotation = transform.rotation;
				rotation = Quaternion.Lerp(currentRotation, targetRotation, 0.02f * zoomDampening * 2);
				transform.rotation = rotation;
			}
			desiredDistance -= Input.GetAxis("Mouse ScrollWheel") * 0.02f * zoomRate * Mathf.Abs(desiredDistance);
			desiredDistance = Mathf.Clamp(desiredDistance, minDistance, maxDistance);
			currentDistance = Mathf.Lerp(currentDistance, desiredDistance, 0.02f * zoomDampening);
			position = target.position - (rotation * Vector3.forward * currentDistance + targetOffset);
			transform.position = position;
		}

		private static float ClampAngle(float angle, float min, float max) {
			if (angle < -360)
				angle += 360;
			if (angle > 360)
				angle -= 360;
			return Mathf.Clamp(angle, min, max);
		}
	}
}
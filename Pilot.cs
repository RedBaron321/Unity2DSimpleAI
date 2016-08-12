using UnityEngine;
using System.Collections;
using System.Collections.Generic;

//[RequireComponent(typeof(Steersman))]
[AddComponentMenu("AI/Pilot")]
public class Pilot : MonoBehaviour
{
    #region Unity scene settings  
	[SerializeField] protected int cycles=1;
	[SerializeField] protected float AngleError = 5f; //ошибка определения угла, отклонения в ее пределах не считаются кораблем
	[SerializeField] protected bool brake=true;
	[SerializeField] protected float slowRadius =2f;
	[SerializeField] protected bool prediction=true;

	[SerializeField] private float nodeRadius = 2f;
	[SerializeField] private float pursuitDistance=10f;

	[SerializeField] protected bool moveAfterRotate=true;

	[SerializeField] protected bool debug=false;

	[Header("Avoidance")]
	[SerializeField] protected bool avoidance=true;
	[SerializeField] private float seeRadius=10f;
	[SerializeField] private float avoidanceVelocityToColliderSize = 3f;
    #endregion

    #region Data 
	protected Engine myEngine;
	protected Rigidbody2D myRigidbody2D;
	protected Transform myTransform;
	protected Rigidbody2D targetRigidbody; //компоненты
	protected Transform targetTransform;
	protected float slowRadiusZnam;
	protected float slowRadiusSqr;
	RaycastHit2D hit;

	private Steersman mySteersman;
	private List<Vector2> nodes;
	private int currentNode=0;

	[System.Serializable]
	protected struct MoveTask
    {
        public float HorInput;
        public float VertInput;
    }
    #endregion

	#region Const
	Vector2 normTop = new Vector2 (0f, 1f);
	Vector2 normBottom = new Vector2 (0f, -1f);
	protected WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();
	#endregion

    #region Methods
	protected void OnEnable() {
		Initialize ();
	}

	protected void OnValidate() {
		if (cycles < 1)
			cycles = 1;
		slowRadiusZnam = 1f / slowRadius;
		slowRadiusSqr = slowRadius * slowRadius;
	}

	protected void Awake() {
		Initialize ();
	}

	protected virtual void Initialize () { //инициализируем все программные компоненты
		myTransform = transform;
		myRigidbody2D = gameObject.GetComponent<Rigidbody2D>();
		myEngine = gameObject.GetComponent<Engine>();
		myEngine.Initialize ();
		mySteersman = gameObject.GetComponent<Steersman>();
		OnValidate ();
		mySteersman.OnNodesRefresh += RefreshNodes; //подписываемся на получение нового списка нод маршрута от штурмана
		StartCoroutine (Control ());
	}
		

	private void RefreshNodes(ref List<Vector2> newNodes) {
		nodes = newNodes;
		currentNode = 0;
	}


	protected IEnumerator Control()
    { //эта функция выбирает стратегию и получает задачу, передавая ее выше в fixedupdate
		int i;
		while (true) {
			MoveToTarget (myRigidbody2D.velocity);
			for (i = 0; i < cycles; i++) {//на случай, если требуется выполнять просчет через регулярные промежутки - cycles, каждый из которых занимает 1 fixedUpdate - 0.02с реального времени
				yield return waitForFixedUpdate;
			}
		}
    }


	protected virtual void MoveToTarget(Vector2 vel)//расчет по алгоритму Крейга Рейнольдса
	{
		Vector2 targetPoint;
		if (targetRigidbody != null) { //если слишком близко - преследуем цель, иначе идем к ближайшей ноде
			if ((targetTransform.position - myTransform.position).sqrMagnitude <= pursuitDistance * pursuitDistance || Mapper.isTargetInsideMap) {
				targetPoint = PosPursuit ();
			} else
				targetPoint = PosPath ();
		} 
		else {
			targetPoint = PosPath ();
		}
		if (debug) Debug.DrawLine (myTransform.position, targetPoint, Color.white); //дебаговая функция для отображения векторов
		targetPoint = myTransform.InverseTransformPoint (targetPoint);

		Vector2 desiredVel;
		if (brake) {
			float distanceSqr = targetPoint.sqrMagnitude;
			if (distanceSqr<=slowRadiusSqr) {//если внутри радиуса торможения - тормозим
				desiredVel = Mathf.Sqrt(distanceSqr)*slowRadiusZnam* targetPoint;
			} 
			else desiredVel = targetPoint;
		}
		else desiredVel = targetPoint;

		if (avoidance)
			Avoidance (ref desiredVel);
		ApplyMoveTask (desiredVel - vel);
	}

	protected virtual void ApplyMoveTask(Vector2 steer) {
		MoveTask task = CalcInput (steer);
		myEngine.Rotate (cycles*task.HorInput); 
		myEngine.Move (cycles*task.VertInput);
	}

	protected MoveTask CalcInput(Vector2 steer) { //переводим усилие в команды газа и поворота
		MoveTask result;
		Vector2 norm;
		if (Vector2.Angle (steer, normTop) <= 90f) {
			norm = normTop;
			result.HorInput = 
				Mathf.Clamp (Mathf.Sign (steer.x) * Vector2.Angle (steer, norm), -myEngine.settings.maxAngle, myEngine.settings.maxAngle);
		} //в какую сторону поворачивать?
		else {
			norm = normBottom;
			result.HorInput = 
				Mathf.Clamp (Mathf.Sign (steer.x) * Vector2.Angle (steer, norm), -myEngine.settings.maxAngle, myEngine.settings.maxAngle);
		}

		if (Vector2.Angle (steer, norm) - Mathf.Abs (result.HorInput)< AngleError || !moveAfterRotate) //если довернули на нужный угол - разгоняемся, иначе ничео не делаем
			result.VertInput = steer.magnitude;
		else
			result.VertInput = 0f;
		return result;
	}
		

	private Vector2 PosPath() {
		if (nodes != null) { 
			if ((nodes[currentNode]- (Vector2)myTransform.position).magnitude <= nodeRadius) {
				currentNode++;
				if (currentNode >= nodes.Count) currentNode = nodes.Count - 1;
			}
			return nodes [currentNode];	
		} //если в радиусе видимости ближайшей ноды - переключаемся на следующую
		else {
			return Vector2.zero;
		}
	}


	protected virtual Vector2 PosPursuit() {
		if (prediction && targetRigidbody != null) {
			return (Vector2)targetRigidbody.position + targetRigidbody.velocity;
		}
		else
			return targetTransform.position;
	}

	void Avoidance (ref Vector2 desiredVelocity) {
		hit = Physics2D.Raycast (myTransform.position, myTransform.TransformDirection(desiredVelocity), seeRadius);
		if (hit.transform!=null && hit.transform!=targetTransform) {
			float avoidanceVelocity = hit.collider.bounds.size.magnitude * avoidanceVelocityToColliderSize;
			desiredVelocity += hit.normal * avoidanceVelocity;	
			if (debug) {
				Debug.DrawLine (myTransform.position, hit.point, Color.yellow);
				Debug.DrawRay (myTransform.position, myTransform.TransformDirection(desiredVelocity), Color.green);
			}
		}
	}
    #endregion

	#region Interface
	public delegate void SendNewTarget(Transform newTarget);
	public event SendNewTarget OnTargetRefresh;

	public void RefreshTarget(Transform newTarget, Rigidbody2D newRigidbody) { //если цель изменится
		targetTransform = newTarget;
		targetRigidbody = newRigidbody;
		if (OnTargetRefresh != null)
			OnTargetRefresh (targetTransform);
	}
	#endregion

}

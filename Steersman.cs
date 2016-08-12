using UnityEngine;
using System.Collections;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif


[AddComponentMenu("AI/Steersman")]
public class Steersman : MonoBehaviour {

	#region Data
	Transform myTransform;
	IntVector2 targetPos, minePos; //позиция бота и корабля игрока на карте волнового алгоритма
	int[,] map; //карта волнового алгоритма
	IntVector2[] route; //массив ячеек, составляющих маршрут до цели (корабля игрока)
	List<Vector2> nodes;
	#endregion

	#region Methods
	void OnEnable() {
		Initialize ();
		Mapper.OnMapRefresh += RefreshMap; //подписываемся на событие обновления карты для получения свежей карты
	}

	void OnDisable() {
		Mapper.OnMapRefresh -= RefreshMap;
	}

	void Initialize () { //начальная инициализация
		myTransform = transform;
		map = new int[Mapper.Size_x, Mapper.Size_y];
		nodes = new List<Vector2> ();
	}

	IntVector2[] FindPath (int[,] main_array, IntVector2 start, IntVector2 target) { 
		//ищет путь до цели методом нахождения ячейки с шагом -1
		IntVector2[] result = new IntVector2[1];
		result[0].x = start.x;
		result[0].y = start.y;
		if (IsCellBlocked(main_array, start.x, start.y)==false){
			if (main_array[start.x, start.y]>0) {
				int length = main_array[start.x, start.y];
				if (length>0) result = new IntVector2[length];
				result[0].x = start.x;
				result[0].y = start.y;
				for (int i = 1; i<result.Length; i++) {
					result [i] = GetMinCell (main_array, result [i - 1].x, result [i - 1].y); //находим минимальную ячейку
				}
			}
		}
		return result;
	}

	void RefreshNodes() {
		nodes = PilotBook.RouteToNodes (route); //переводим маршрут в список ориентиров-нод
		if (OnNodesRefresh != null)
			OnNodesRefresh (ref nodes);
	}

	void Calcroute () { //обновление маршрута
		targetPos = Mapper.TargetCell;
		minePos = Mapper.CoordinatesInMap (new Vector2 (myTransform.position.x, myTransform.position.y));
		route = FindPath (map, minePos, targetPos);
		RefreshNodes ();
	}

	bool IsCellBlocked (int[,] main_array, int x, int y) {//блокирована ли ячейка вокруг препятствиями
		bool result = true;
		IntVector2[] around = new IntVector2[0];
		Mapper.GetCellsAround(main_array, x, y, out around);
		for (int i=0; i<around.Length; i++) {
			if (main_array[around[i].x, around[i].y]!=-2) {
				result = false;
				break;
			}
		}
		return result;
	}

	void RefreshMap (ref int[,] newMap) {
		map = newMap;
		Calcroute ();
	}
		

	IntVector2 GetMinCell (int[,] main_array, int x, int y) { //найти ячейку с минимальным значением вокруг заданной
		IntVector2 result = IntVector2.zero;
		IntVector2[] around = new IntVector2[0];
		Mapper.GetCellsAround(main_array, x, y, out around);
		int step = main_array [x, y];
		for (int i = 0; i<around.Length; i++) {
			if ((main_array [around [i].x, around [i].y]>=0)&(main_array [around [i].x, around [i].y]<step)) {
				step = main_array [around [i].x, around [i].y];
				result.x = around [i].x;
				result.y = around [i].y;
			}
		}
		return result;
	}
	#endregion

	#region Interface
	public delegate void SendNewNodes(ref List<Vector2> newNodes);
	public event SendNewNodes OnNodesRefresh;

	#if UNITY_EDITOR_WIN
	public void Visualise(bool withRoute) {
		int Size_x = map.GetLength (0);
		int Size_y = map.GetLength (1);
		Texture2D tex = new Texture2D (Size_x, Size_y);
		Color pixelColor;
		for (int i = 0; i < Size_x; i++) {
			for (int j = 0; j < Size_y; j++) {
				if (map[i,j]==-2) pixelColor = Color.black;
				else pixelColor = Color.white;
				tex.SetPixel(i,j, pixelColor);
			}
		}
		if (withRoute) {
			if (route != null && route.Length > 0) {
				for (int i = 0; i < route.Length; i++) {
					tex.SetPixel (route [i].x, route [i].y, Color.red);
				}
			} else
				Debug.Log ("Route null!");
			IntVector2 currentCoords;
			for (int i = 0; i < nodes.Count; i++) {
				currentCoords = Mapper.CoordinatesInMap (nodes [i]);
				tex.SetPixel (currentCoords.x, currentCoords.y, Color.yellow);
			}
		}
		byte[] bytes = tex.EncodeToPNG();
		File.WriteAllBytes("D://out.png", bytes);

	}
	#endif


	#endregion
	
}


#if UNITY_EDITOR_WIN
[CustomEditor(typeof(Steersman))]
public class SteersmanEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		Steersman myScript = (Steersman)target;
		if(GUILayout.Button("VisualizeWithRoute"))
		{
			myScript.Visualise (true);
		}
		if(GUILayout.Button("Visualize"))
		{
			myScript.Visualise (false);
		}
	}
}
#endif
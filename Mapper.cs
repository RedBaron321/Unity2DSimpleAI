using UnityEngine;
using System.Collections;
using System.IO;
using System.Threading;

public static class Mapper{
	#region Data
	static int[,] Map; //собственно, карта
	static bool[,] BusyMap; //карта препятствий, обновляется реже, чем обычная карта
	static float LBX = 0f; //промежуточные координаты
	static float LBY = 0f;
	static Vector2[,] MapTransform;//массив углов
	static Collider2D[] InterCol;//промежуточный массив
	static bool refreshThreadBusy=false;
	static bool freshMap = false; //флаг второго потока
	static int maxCells;
	static BusyCell [][] areas;
	static int busyPartsCounter = 0;
	#region Multithreading
	static Thread thread;
	static Vector2 targetPosition;
	static int refreshPeriod = 2000; //время сна второго потока при обновлении
	#endregion
	#endregion

	#region Interface
	public delegate void SendNewMap(ref int[,] newMap);
	public static event SendNewMap OnMapRefresh;

	public static float X; //размер глобальной карты в юнитах
	public static float Y;
	public static float CellSize = 1f; //размер одной ячейки в юнитах
	public static CreateLevel CL;
	public static int Size_x; //размер карты для волнового алгоритма в клетках
	public static int Size_y;
	public static Transform Target; //цель, к которой ищем путь и от которой распространяется волна
	public static IntVector2 TargetCell; //то же, что и  Vector2, но с целыми переменными, чтобы не преобразовывать типы
	public static bool isTargetInsideMap=false;

	struct BusyCell {
		public bool value;
		public IntVector2 pos;
	}


	#if UNITY_EDITOR_WIN
	public static void VisualiseMap(string path) {
		Texture2D tex = new Texture2D (Size_x, Size_y);
		Color pixelColor;
		for (int i = 0; i < Size_x; i++) {
			for (int j = 0; j < Size_y; j++) {
				if (BusyMap[i,j]==true) pixelColor = Color.black;
				else pixelColor = Color.white;
				tex.SetPixel(i,j, pixelColor);
			}
		}
		byte[] bytes = tex.EncodeToPNG();
		File.WriteAllBytes(path+"/out.png", bytes);
	}
	#endif

	public static void StartRefreshThread(int threadPeriod) { //функция запускает второй поток, занимающий регулярным перерасчетом карты волнового алгоритма
		refreshPeriod = threadPeriod;
		thread = new Thread (RefreshMap);
		thread.Start ();
	}

	public static void CreateMap (CreateLevel cl, float cell_size, Transform target, int portion) {//инициализация
		maxCells = portion;
		InterCol = new Collider2D[3];
		CL = cl;
		CellSize = cell_size;
		Target = target;
		X = CreateLevel.X;
		Y = CreateLevel.Y;
		Size_x = Mathf.CeilToInt(X / CellSize);
		Size_y = Mathf.CeilToInt(Y / CellSize); //определяем границы
		AllocateCells ();  //разделяем на части для частичного обновления карты препятствий
		LBX = -Size_x/2; 
		LBY = -Size_y/2; //координаты левого нижнего угла каждой ячейки для связи с transform-координатами
		Map = new int[Size_x, Size_y];
		BusyMap = new bool[Size_x, Size_y];
		MapTransform = new Vector2[Size_x, Size_y];//массив углов
	}



	public static void RefreshBusyMap (ref int mask) { /*обновлять карту препятствий дороже по производительности, 
	поэтому она вынесена в отдельную функцию и обновляется реже
	*/
		lock (BusyMap) {
			for (int i =0; i< Size_x; i++) {
				LBX = -X/2+CellSize*i;
				for (int z = 0; z<Size_y; z++) {
					LBY = -Y/2+CellSize*z;
					if (IsAreaBusy(LBX,LBY,CellSize,CellSize, InterCol, mask)==true)   {
						BusyMap[i,z] = true; //-2 - занят
					}
					else {
						BusyMap[i,z] = false;//-1 - не отмечен
					}
				}
			}
		}
	}

	public static void RefreshBusyMapPart(ref int mask) { //обновляет часть карты препятствий
		lock (BusyMap) {
			for (int i = 0; i < areas [busyPartsCounter].Length; i++) {
				LBX = -X / 2 + CellSize * areas [busyPartsCounter] [i].pos.x;
				LBY = -Y / 2 + CellSize * areas [busyPartsCounter][i].pos.y;
				if (IsAreaBusy(LBX,LBY,CellSize,CellSize, InterCol, mask)==true)   {
					BusyMap[areas [busyPartsCounter] [i].pos.x,areas [busyPartsCounter] [i].pos.y] = true; //-2 - занят
				}
				else {
					BusyMap[areas [busyPartsCounter] [i].pos.x,areas [busyPartsCounter] [i].pos.y] = false;//-1 - не отмечен
				}
			}
			busyPartsCounter = Mathf.Clamp (busyPartsCounter + 1, 0, areas.GetLength (0) - 1);
		}
	}

	public static void RefreshMapState(Vector2 targetPos) {
		if (!refreshThreadBusy) {
			targetPosition = targetPos;
			if (OnMapRefresh != null && freshMap) {
				OnMapRefresh (ref Map);
				freshMap = false;
			}
		}
	}


	public static IntVector2 CoordinatesInMap (Vector2 Coordinates) {//определяет, в какой ячейке находится объект с координатами Coordinates
		for (int i = 0; i<MapTransform.GetLength(0); i++) {
			for (int z=0; z<MapTransform.GetLength(1); z++) {
				if ((Coordinates.x>=MapTransform[i,z].x && Coordinates.x<=MapTransform[i,z].x+CellSize) &&
					(Coordinates.y>=MapTransform[i,z].y && Coordinates.y<=MapTransform[i,z].y+CellSize)) {
						return new IntVector2(i, z);
				}
			}
		}
		return IntVector2.zero;
	}



	public static void GetCellsAround (int[,] main_array, int x, int y, out IntVector2[] return_array) { /*
возвращает по ссылке массив ячеек вокруг выбранной с координатами x, y в массиве main_array
    */
		int number;
		if ((x == 0) || (x == main_array.GetLength (0) - 1)) {
			if ((y == 0) || (y == main_array.GetLength (1) - 1)) {
				number = 3;
			} else {
				number = 5;
			}
		} 
		else if ((y == 0) || (y == main_array.GetLength (1) - 1)) {
			number = 5;
		} 
		else {
			number = 8; //тут определяем размер возвращаемого массива, чтобы не выйти за границы main_array (ячейка может и на краю находиться)
		}
		int x_length = main_array.GetLength (0);
		int y_length = main_array.GetLength (1);
		return_array = new IntVector2[number];
		number = 0;
		if ((x + 1 <x_length)&(y + 1 <y_length)) {
			return_array[number]=new IntVector2(x+1, y+1);
			number++;
		}
		if ((x + 1 <x_length)) {
			return_array[number]=new IntVector2(x+1, y);
			number++;
		}
		if ((x + 1 <x_length) & (y - 1 >= 0)) {
			return_array[number]=new IntVector2(x+1, y-1);
			number++;
		}
		if ((y - 1 >= 0)) {
			return_array[number]=new IntVector2(x, y-1);
			number++;
		}
		if ((x - 1 >= 0) & (y - 1 >= 0)) {
			return_array[number]=new IntVector2(x-1, y-1);
			number++;
		}
		if ((x - 1 >= 0)) {
			return_array[number]=new IntVector2(x-1, y);
			number++;
		}
		if ((x - 1 >= 0) & (y + 1 <y_length)) {
			return_array[number]=new IntVector2(x-1, y+1);
			number++;
		}
		if ((y + 1 <y_length)) {
			return_array[number]=new IntVector2(x, y+1); //говнокод, ну а как еще-то проверить, существуют ли 8 ячеек вокруг?
			number++;
		}
	}

	public static bool InMap(Vector2 coords) {
		return ((coords.x < X *0.5f && coords.x > -X *0.5f) && (coords.y < Y *0.5f && coords.y > -Y *0.5f));
	}
	#endregion

	#region Methods
	static void AllocateCells() { //разбивает карту на районы для последующего частичного обновления
		areas = new BusyCell[Mathf.CeilToInt((float)(Size_x * Size_y) /(float) maxCells)][];
		//Debug.Log (areas.GetLength (0));

		for (int i = 0; i < areas.GetLength (0) - 1; i++) {
			areas [i] = new BusyCell[maxCells];
		}
		areas [areas.GetLength (0) - 1] = new BusyCell[Size_x*Size_y - (areas.GetLength(0)-1)*maxCells];
		int partsCount = 0;
		int cellsCount = 0;
		for (int i = 0; i < Size_x; i++) {
			for (int j = 0; j < Size_y; j++) {
				areas [partsCount] [cellsCount].pos.x = i;
				areas [partsCount] [cellsCount].pos.y = j;
				cellsCount++;
				if (cellsCount >= maxCells) {
					partsCount++;
					cellsCount = 0;
				}
			}
		}
		busyPartsCounter = 0;
	}

	static void RefreshMap () { //разметка карты, отметка занятых ячеек
		while (true) {
			refreshThreadBusy = true;
			lock (Map) {
				for (int i =0; i< Size_x; i++) {
					LBX = -X/2+CellSize*i;
					for (int z = 0; z<Size_y; z++) {
						LBY = -Y/2+CellSize*z;
						MapTransform[i, z].x = LBX;
						MapTransform[i,z].y = LBY;
						if (BusyMap[i,z]==true)   {
							Map[i,z] = -2; //-2 - занят
						}
						else {
							Map[i,z] = -1;//-1 - не отмечен
						}
					}
				}
				isTargetInsideMap = InMap (targetPosition);
				if (isTargetInsideMap)
				TargetCell = CoordinatesInMap (targetPosition); //определяем координаты цели
				else TargetCell = new IntVector2(Size_x/2, Size_y/2);
				Map[TargetCell.x, TargetCell.y] = 0; // 0 - цель
				MarkAll (Map, TargetCell.x, TargetCell.y); //распространяем волну
			}
			refreshThreadBusy = false;
			freshMap = true;
			Thread.Sleep (refreshPeriod);
		}
	}

	static bool IsAreaBusy (float LeftBottomX, float LeftBottomY, float LengthX, float LengthY, Collider2D[] ResultCol, int Mask) {/*
	проверяет занята ли ячейка чем-то, кроме объектов в layer ship
	*/
		Vector2 pointA;
		Vector2 pointB;
		pointA.x = LeftBottomX;
		pointA.y = LeftBottomY;
		pointB.x = LeftBottomX + LengthX;
		pointB.y = LeftBottomY + LengthY;
		int col = Physics2D.OverlapAreaNonAlloc (pointA, pointB, ResultCol, Mask); //наименее тяжелая функция для проверки на существование коллайдера в зоне
		if (col == 0) {
			return false;
		} else
			return true;
	}


	static void MarkAround (int[,] main_array, int x, int y) {//помечает все незанятые проходимые ячейки вокруг выбранной
		int step = main_array [x, y];
		IntVector2[] cells_around = new IntVector2[0];
		GetCellsAround (main_array, x, y, out cells_around);
		for (int i=0; i<cells_around.Length; i++) {
			if (main_array[cells_around[i].x, cells_around[i].y]==-1) {
				main_array[cells_around[i].x, cells_around[i].y] = step+1;
			}
		}
	}


	static void MarkAll (int[,] main_array, int target_x, int target_y) { //пометить массив целиком для волнового алгоритма, распространение волны
		MarkAround (main_array, target_x, target_y);
		int step = 1;
		bool add = false;
		bool stop = false;
		int Ni = 0;
		int Nmax = main_array.GetLength (0) * main_array.GetLength (1);
		while (stop==false) {
			add = false;
			for (int i = 0; i<main_array.GetLength(0); i++) {
				for (int j = 0; j<main_array.GetLength(1); j++) {
					if (main_array[i,j]==step) {
						MarkAround(main_array, i, j);
						add = true;
					}
				}
			}
			Ni ++;
			if (add==true) {step++;}
			if ((Ni>Nmax)||(add==false)) {//количество итераций превысило максимальное, либо мы прошлись по массиву и ничего не отметили (волна закончилась)
				stop = true;
			}
		}
	}
	#endregion

}

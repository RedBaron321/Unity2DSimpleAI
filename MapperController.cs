using UnityEngine;
using System.Collections;
using Extensions;
//using System.Threading;

[AddComponentMenu("AI/MapperController")]
public class MapperController : MonoBehaviour {
	
	#region Unity scene settings
	[SerializeField] private Transform target; //компонент Transform корабля игрока
	public MapperControllerSettings settings;

	[System.Serializable] public struct MapperControllerSettings {
		public float cellSize;//размер одной ячейки, отвечает за точность составления карты волнового алгоритма
		public float refreshMapPeriod;
		public float refreshBusyMapPeriod;
		public int maxCellsCheck;
		public float threadPeriod;
		public bool[] mask;
	}
	#endregion

	#region Data
	int intMask=0;
	#endregion

	#region Methods
	void OnValidate() {
		if (settings.mask == null) {
			settings.mask = new bool[32];	
		}
		if (settings.mask.Length!=32) settings.mask = new bool[32];
		intMask = AFunc.BitsToInt (settings.mask);
		if (settings.maxCellsCheck < 1)
			settings.maxCellsCheck = 1;
	}
		


	IEnumerator RefreshBusyMap (float period) { //функция обновления карты препятствий статического класса Mapper
		Mapper.RefreshBusyMap (ref intMask); //передаем по ссылке слой для обновления (разделение по слоям в целях оптимизации)
		while (true) {
			Mapper.RefreshBusyMapPart(ref intMask); //Mapper обновляет следующую в очереди часть карты препятствий (по частям в целях оптимизации)
			yield return new WaitForSeconds (period); //перерыв
		}
	}
			
	IEnumerator RefreshMap (float period) {//функция обновления карты волнового алгоритма статического класса Mapper
		while (true) {
			Mapper.RefreshMapState (target.position);//проверяем, готова ли свежая карта
			yield return new WaitForSeconds (period);//перерыв
		}
	}
    #endregion

	#region Interface
	public void StartRefresh() { //инициализация Mapper-а, запускаем сопрограммы
		Mapper.CreateMap (CreateLevel.instance, settings.cellSize, target, settings.maxCellsCheck);
		Mapper.StartRefreshThread (Mathf.RoundToInt (settings.threadPeriod * 1000f));
		StartCoroutine (RefreshBusyMap (settings.refreshBusyMapPeriod)); //сопрограмма, регулярно обновляющая карту препятствий
		StartCoroutine (RefreshMap (settings.	refreshMapPeriod)); //сопрограмма, проверяющая, есть ли свежая карта
	}
	#endregion
}

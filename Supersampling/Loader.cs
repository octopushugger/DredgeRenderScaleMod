using UnityEngine;

namespace Supersampling
{
	public class Loader
	{
		/// <summary>
		/// This method is run by Winch to initialize your mod
		/// </summary>
		public static void Initialize()
		{
			var gameObject = new GameObject(nameof(Supersampling));
			gameObject.AddComponent<Supersampling>();
			GameObject.DontDestroyOnLoad(gameObject);
		}
	}
}
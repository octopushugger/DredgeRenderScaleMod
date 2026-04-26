using UnityEngine;
using Winch.Core;

namespace Supersampling
{
	public class Supersampling : MonoBehaviour
	{
		public void Awake()
		{
			WinchCore.Log.Debug($"{nameof(Supersampling)} has loaded!");
		}
	}
}

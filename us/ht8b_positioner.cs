
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_positioner : UdonSharpBehaviour {

[SerializeField] ht8b main;

// Since v0.3.0: OnPickupUseDown -> OnDrop
void OnDrop()
{
	main._tr_placeball();
}

}
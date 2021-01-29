
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_otherhand : UdonSharpBehaviour {

[SerializeField] GameObject objPrimary;
ht8b_cue usPrimary;

Vector3 originalDelta;
bool isHolding = false;
public bool bOtherHold = false;  // Primary is being held
Vector3 lockpos;

private void OnPickupUseDown()
{
	usPrimary._otherlock();
	lockpos = this.transform.position;
}

private void OnPickupUseUp()	// VR
{
	usPrimary._otherunlock();
}

private void OnPickup()
{
	isHolding = true;
}

private void OnDrop()
{
	originalDelta = objPrimary.transform.InverseTransformPoint( this.transform.position );

	// Clamp within 1 meters in case something got messed up
	if( originalDelta.sqrMagnitude > 0.6084f )
	{
		originalDelta = originalDelta.normalized * 0.78f;
	}

	isHolding = false;
	usPrimary._otherunlock();
}

private void Update()
{
	// Pseudo-parented while it left is let go
	if( !isHolding && bOtherHold )
	{
		this.transform.position = objPrimary.transform.TransformPoint( originalDelta );
	}
}

private void Start()
{
	usPrimary = objPrimary.GetComponent<ht8b_cue>();
	OnDrop();	
}
}

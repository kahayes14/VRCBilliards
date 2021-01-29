#if !UNITY_ANDROID
#define HT8B_DEBUGGER
#else
#define HT_QUEST
#endif

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_cue : UdonSharpBehaviour
{
	[SerializeField]
	public GameObject objTarget;
	public ht8b_otherhand objTargetController;

	[SerializeField]
	public GameObject objCue;

	[SerializeField]
	public ht8b gameController;

	[SerializeField]
	public GameObject objTip;

	[SerializeField] GameObject ui_pressE;

	// Pickuip components
	private VRC_Pickup pickup_this;
	private VRC_Pickup pickup_target;

	VRCPlayerApi localplayer;

#if !HT_QUEST

	// Make desktop mode a bit easier
	public bool useDesktop = false;


	public bool dkPrimaryControl = true;

#endif

	// ( Experimental ) Allow player ownership autoswitching routine 
	public bool bAllowAutoSwitch = true;
	public int playerID = 0;
	
	Vector3 lag_objTarget;
	Vector3 lag_objBase;

	Vector3 vBase;
	Vector3 vLineNorm;
	Vector3 targetOriginalDelta;

	Vector3 vSnOff;
	float vSnDet;

	bool bArmed = false;
	bool bHolding = false;
	bool bOtherLock = false;

	Vector3 reset_pos_this;
	Vector3 reset_pos_target;

	Vector3 dkCursorPos = Vector3.zero;

	private void OnPickupUseDown()
	{
#if !HT_QUEST
		if( !useDesktop )	// VR only controls
		{
#endif
			bArmed = true;

			// copy target position in
			vBase = this.transform.position;

			// Set up line normal
			vLineNorm = (objTarget.transform.position - vBase).normalized;

			// It should now be able to impulse ball
			gameController._tr_starthit();

#if !HT_QUEST
		}
#endif
	}
	private void OnPickupUseUp()	// VR
	{

#if !HT_QUEST
		if( !useDesktop )
		{

			bArmed = false;

			gameController._tr_endhit();
		}
#else
		bArmed = false;
		gameController._tr_endhit();
#endif

	}

	private void OnPickup()
	{
#if !HT_QUEST
		if( !useDesktop )	// We dont need other hand to be availible for desktop player
		{
			objTarget.transform.localScale = Vector3.one;
		}
#else
		objTarget.transform.localScale = Vector3.one;
#endif

		// Register the cuetip with main game
		// gameController.cuetip = objTip; 

		// Not sure if this is necessary to do both since we pickup this one,
		// but just to be safe
		Networking.SetOwner( Networking.LocalPlayer, this.gameObject );
		Networking.SetOwner( Networking.LocalPlayer, objTarget );
		bHolding = true;
		objTargetController.bOtherHold = true;
		objTarget.GetComponent<SphereCollider>().enabled = true;
	}

	private void OnDrop()
	{
		objTarget.transform.localScale = Vector3.zero;
		bHolding = false;
		objTargetController.bOtherHold = false;
		objTarget.GetComponent<SphereCollider>().enabled = false;

		#if !HT_QUEST
		if( useDesktop )
		{
			ui_pressE.SetActive( false );
			gameController._ht_desktop_cue_down();
		}

		#endif
	}

	private void Start()
	{
		// Match lerped positions at start
		lag_objBase = this.transform.position;
		lag_objTarget = objTarget.transform.position;

		targetOriginalDelta = this.transform.InverseTransformPoint( objTarget.transform.position );
		OnDrop();

		pickup_this = (VRC_Pickup)this.gameObject.GetComponent(typeof(VRC_Pickup));
		pickup_target = (VRC_Pickup)objTarget.GetComponent(typeof(VRC_Pickup));

		reset_pos_target = objTarget.transform.position;
		reset_pos_this = this.transform.position;

		localplayer = Networking.LocalPlayer;
	}

	// Set if local player can hold onto cue grips or not
	public void _access_allow()
	{
		this.GetComponent<SphereCollider>().enabled = true;
		objTarget.GetComponent<SphereCollider>().enabled = true;
	}

	public void _access_deny()
	{
		// Put back on the table
		objTarget.transform.position = reset_pos_target;
		this.transform.position = reset_pos_this;

		this.GetComponent<SphereCollider>().enabled = false;
		objTarget.GetComponent<SphereCollider>().enabled = false;

		// Force user to drop it
		pickup_this.Drop();
		pickup_target.Drop();
	}

	public void _otherlock()
	{
		bOtherLock = true;
	}

	public void _otherunlock()
	{
		bOtherLock = false;
	}

	void Update()
	{
		// Put cue in hand
		#if !HT_QUEST
		if( dkPrimaryControl )
		{
			if( useDesktop && bHolding )
			{
				this.transform.position = Networking.LocalPlayer.GetBonePosition( HumanBodyBones.RightHand );

				// Temporary target
				objTarget.transform.position = this.transform.position + Vector3.up;

				Vector3 playerpos = gameController.gameObject.transform.InverseTransformPoint( Networking.LocalPlayer.GetPosition() );
					
				// Check turn entry
				if( (Mathf.Abs( playerpos.x ) < 2.0f) && (Mathf.Abs( playerpos.z ) < 1.5f) )
				{
					VRCPlayerApi.TrackingData hmd = localplayer.GetTrackingData( VRCPlayerApi.TrackingDataType.Head );
					ui_pressE.SetActive( true );
					ui_pressE.transform.position = hmd.position + hmd.rotation * Vector3.forward;
					if( Input.GetKeyDown( KeyCode.E ) )
					{
						dkPrimaryControl = false;
						gameController._ht_desktop_enter();
					}
				}
				else
				{
					ui_pressE.SetActive( false );
				}
			}

			lag_objBase = Vector3.Lerp( lag_objBase, this.transform.position, Time.deltaTime * 16.0f );
		#endif

			if( !bOtherLock )
				lag_objTarget = Vector3.Lerp( lag_objTarget, objTarget.transform.position, Time.deltaTime * 16.0f );

			if( bArmed )
			{
				vSnOff = lag_objBase - vBase;
				vSnDet = Vector3.Dot( vSnOff, vLineNorm );
				objCue.transform.position = vBase + vLineNorm * vSnDet;
			}
			else
			{
				// put cue at base position	
				objCue.transform.position = lag_objBase;
				objCue.transform.LookAt( lag_objTarget );
			}

		#if !HT_QUEST
		}
		#endif

		if( bHolding )
		{
			// Clamp controllers to play boundaries while we have hold of them
			Vector3 temp = this.transform.localPosition;
			temp.x = Mathf.Clamp( temp.x, -4.0f, 4.0f );
			temp.y = Mathf.Clamp( temp.y, -0.8f, 1.5f );
			temp.z = Mathf.Clamp( temp.z, -3.25f, 3.25f );
			this.transform.localPosition = temp;
			temp = objTarget.transform.localPosition;
			temp.x = Mathf.Clamp( temp.x, -4.0f, 4.0f );
			temp.y = Mathf.Clamp( temp.y, -0.8f, 1.5f );
			temp.z = Mathf.Clamp( temp.z, -3.25f, 3.25f );
			objTarget.transform.localPosition = temp;
		}
	}
}

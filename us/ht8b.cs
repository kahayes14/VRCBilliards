/* 
 https://www.harrygodden.com

 live:	wrld_08badc69-7665-4dc5-8243-3867455dc17c
 dev:		wrld_9497c2da-97ee-4b2e-9f82-f9adb024b6fe

 Update log:
	16.12.2020 (0.1.3a)	-	Fix for new game, wrong local turn colour
								-	Fix for not losing match when scratch on pot 8 ball				( Thanks: Photographotter, Mystical )
								-	Added permission info to console when fail reset
	17.12.2020 (0.2.0a)	-	Predictive physics for cue ball
								-  Fix for not winning when sink 8, and objective on same turn		( Thanks: Rosy )
								-	Reduced code spaghet in decode routine
								-	Improved algorithm for post-game state checking, should lend
								   to easier implementation of optional rules.
								-	Allow colour switching between UK/USA/Default colour sets
								-  Grips change colour based on which turn it is
					0.3.0a	-	Added desktop mode
								-	Sink opponents ball = loss turn
								-	Removed coloursets
					0.3.1a	-	Desktop QOL
					0.3.2a	-	Reduced sensitivity
								-	Added pad bytes
					0.3.7a	-	Quest support
					0.3.8a	-	Switched network string to base64 encoded
								-	Changed initial break setup
					1.0.0		-	First full Release

 Networking Model Information:
	
	This implementation of 8 ball is based around passing ownership between clients who are
	playing the game. A player is 'registered' into the game when they have ownership of one
	of the two player 'totems'. In this implementation the totems are the pool cues themselves.

	When a turn ends, the player who is currently playing will pack information into the 
	networking string that the turn has been transferred, and once the remote client who is
	associated with the opposite cue recieves the update, they will take ownership of the main
	script.

	The local player will have a 'permit' to shoot when it is their turn, which allows them
	to interact with the physics world. As soon as the cue ball is shot, the script calculates
	and compresses the necessery velocities and positions of the balls, and 1. sends that out
	to remote clients, and 2. decodes it the same way themselves. So effectively all players
	end up watching the exact same simulation at very close to the same time. In testing this
	was immediate as it could be with a GB -> USA connection.

 Information about the data:

	- Data is transfered using 1 Udon Synced string which is 82 bytes long, encoded to base64( 110 bytes )
	- Critical game states are packed into a bitmask at #19
	- Floating point positions are encoded/decoded as follows:
		Encode:
			Divide the value by the expected maximum range
			Multiply that by signed short max value ~32k
			Add signed short max
			Cast to ushort
		Decode:
			Cast ushort to float
			Subtract short max
			Divide by short max
			Multiply by the same range encoded with

	- Ball ID's are designed around bitmasks and are as follows:

	byte | Byte 0														| Byte 1														|
	bit  | x80 . x40 . x20 . x10 . x08 . x04 . x02	| x1 .. x80 . x40 . x20 . x10 . x08 . x04 | x02 | x01 |
	ball | 15	 14	 13    12    11    10    9    |  7     6     5     4     3    2     1   |  8  | cue |

 Networking Layout:

   Total size: 78 bytes over network // 39 C# wchar
 
   Address		What						Data type
  
	[ 0x00  ]	ball positions			(compressed quantized vec2's)
	[ 0x40  ]	cue ball velocity		^
	[ 0x44  ]	cue ball angular vel	^

	[ 0x4A  ]	sn_pocketed				uint16 bitmask ( above table )
				OR	sn_gmspec				| bit	#	| mask	| what				|
												| 0-3		| 0x0f	| fb_scores[ 0 ]	|
												| 4-7		| 0xf0	| fb_scores[ 1	]	|
	
	[ 0x4C  ]	game state flags		| bit #	| mask	| what				| 
												| 0		| 0x1		| sn_simulating	|
												| 1		| 0x2		| sn_turnid			|
												| 2		| 0x4		| sn_foul			|
												| 3		| 0x8		| sn_open			|
												| 4		| 0x10	| sn_playerxor		|
												| 5		| 0x20	| sn_gameover		|
												| 6		| 0x40	| sn_winnerid		|
												| 7		| 0x80	| sn_permit			|
												| 8-10	| 0x700	| sn_gamemode		|
												| 11		| 0x800  | sn_lobbyopen		|
												| 12		| 0x1000 | <reserved>		|
												| 13-14	| 0x6000 | sn_timer			|
												| 15		| 0x8000 | sn_allowteams	|
												
	[ 0x4E  ]	packet #					uint16
	[ 0x50  ]	gameid					uint16

 Physics Implementation:
	
	Physics are done in 2D to save instructions. The implementation is designed to be
	as numerically stable as possible (eg. using linear algebra as much as possible to
	be explicit about what and where stuff collides ).

	Ball physic response is 100% pure elastic energy transfer, which even at one iteration
	per physics update seems to give plausable enough results. balls can behave like a 
	newtons cradle which is what we want.

	Edge collisions are a little contrived and the reason why the table can ONLY be placed
	at world orign. the table is divided into major and minor sections. some of the 
	calculations can be peeked at here: https://www.geogebra.org/m/jcteyvj6 . It is all
	straight line equations.
	
	There MAY be deviations between SOME client cpus / platforms depending on the floating 
	point architecture, and who knows what the fuck C# will decide to do at runtime anyway. 
	However after some testing this seems rare enough that we could not observe any
	differences at all. If it does happen to be calculated differently, the remote clients
	will catch up with the players game anyway. I reckon this is most likely going to
	affect, if it does at all, only quest/pc crossplay and not much else.

	Physics are calculated on a fixed timestep, using accumulator model. If there is very
	low framerate physics may run at a slower timescale if it passes the threshold where
	maximum updates/frame is reached, but won't affect eventual outcome.
	
	The display balls have their position matched, and rotated based on pure rolling model.
*/

// https://feedback.vrchat.com/feature-requests/p/udon-expose-shaderpropertytoid
// #define USE_INT_UNIFORMS

// Currently unstable..
// #define HT8B_ALLOW_AUTOSWITCH

#if !UNITY_ANDROID
#define HT8B_DEBUGGER
#else
#define HT_QUEST
#endif

//#define COMPILE_FUNC_TESTS
//#define MULTIGAMES_PORTAL
//#define COMPILE_FUNC_TESTS
#define MENU_DEV

using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using System;
using UnityEngine.Rendering;

public class ht8b : UdonSharpBehaviour {

#region R_CONSTANTS

#if HT_QUEST
const float k_MAX_DELTA = 0.075f;						// Maximum steps/frame ( 5 ish )
#else
const float k_MAX_DELTA = 0.1f;						// Maximum steps/frame ( 8 )
#endif

// Physics calculation constants (measurements are in meters)

const float k_FIXED_TIME_STEP = 0.0125f;					// time step in seconds per iteration
const float k_FIXED_SUBSTEP	= 0.00125f;
const float k_TIME_ALPHA		= 50.0f;						// (unused) physics interpolation
const float k_TABLE_WIDTH		= 1.0668f;					// horizontal span of table
const float k_TABLE_HEIGHT		= 0.6096f;					// vertical span of table
const float k_BALL_DIAMETRE	= 0.06f;						// width of ball
const float k_BALL_PL_X			= 0.03f;						// break placement X
const float k_BALL_PL_Y			= 0.05196152422f;			// Break placement Y
const float k_BALL_1OR			= 33.3333333333f;			// 1 over ball radius
const float k_BALL_RSQR			= 0.0009f;					// ball radius squared
const float k_BALL_DSQR			= 0.0036f;					// ball diameter squared
const float k_BALL_DSQRPE		= 0.003598f;				// ball diameter squared plus epsilon
const float k_POCKET_RADIUS	= 0.09f;						// Full diameter of pockets (exc ball radi)
const float k_CUSHION_RSTT		= 0.79f;						// Coefficient of restituion against cushion

const float k_1OR2				= 0.70710678118f;			// 1 over root 2 (normalize +-1,+-1 vector)
const float k_1OR5				= 0.4472135955f;			// 1 over root 5 (normalize +-1,+-2 vector)
const float k_RANDOMIZE_F		= 0.0001f;

const float k_POCKET_DEPTH		= 0.04f;						// How far back (roughly) do pockets absorb balls after this point
const float k_MIN_VELOCITY		= 0.00005625f;				// SQUARED

const float k_FRICTION_EFF		= 0.99f;						// How much to multiply velocity by each update

const float k_F_SLIDE			= 0.2f;						// Friction coefficient of sliding
const float k_F_ROLL				= 0.01f;						// Friction coefficient of rolling

const float k_SPOT_POSITION_X = 0.5334f;					// First X position of the racked balls
const float k_SPOT_CAROM_X		= 0.8001f;					// Spot position for carom mode

const float k_RACK_HEIGHT		= -0.0702f;					// Rack position on Y axis
const float k_GRAVITY			= 9.80665f;					// Earths gravitational acceleration
const float k_BALL_MASS			= 0.16f;						// Weight of ball in kg

public bool IS_ROLLING = false;

Vector3 k_CONTACT_POINT = new Vector3( 0.0f, -0.03f, 0.0f );

#if HT_QUEST
uint ANDROID_UNIFORM_CLOCK = 0x00u;
uint ANDROID_CLOCK_DIVIDER = 0x8u;
#endif

// Colour constants
Color k_tableColourBlue		= new Color( 0.0f, 0.75f, 1.75f, 1.0f ); // Presets ..
Color k_tableColourOrange	= new Color( 1.75f, 0.25f, 0.0f, 1.0f );
Color k_tableColourRed		= new Color( 1.2f, 0.0f, 0.0f, 1.0f );
Color k_tableColorWhite		= new Color( 1.0f, 1.0f, 1.0f, 1.0f );
Color k_tableColourBlack	= new Color( 0.01f, 0.01f, 0.01f, 1.0f );
Color k_tableColourYellow	= new Color( 2.0f, 1.0f, 0.0f, 1.0f );
Color k_tableColourPink		= new Color( 2.0f, 0.0f, 1.5f, 1.0f );
Color k_tableColourGreen	= new Color( 0.0f, 2.0f, 0.0f, 1.0f );
Color k_tableColourLBlue	= new Color( 0.3f, 0.6f, 1.0f, 1.0f );

Color k_markerColourOK		= new Color( 0.0f, 1.0f, 0.0f, 1.0f );
Color k_markerColourNO		= new Color( 1.0f, 0.0f, 0.0f, 1.0f );

Color k_gripColourActive	= new Color( 0.0f, 0.5f, 1.1f, 1.0f );
Color k_gripColourInactive = new Color( 0.34f, 0.34f, 0.34f, 1.0f );

Color k_fabricColour_gray	= new Color( 0.3f, 0.3f, 0.3f, 1.0f );
Color k_fabricColour_red	= new Color( 0.9f, 0.2f, 0.1f, 1.0f );
Color k_fabricColour_blue	= new Color( 0.1f, 0.6f, 1.0f, 1.0f );
Color k_fabricColour_white = new Color( 0.8f, 0.8f, 0.8f, 1.0f );
Color k_fabricColour_green = new Color( 0.15f, 0.75f, 0.3f, 1.0f );

Color k_aimColour_aim		= new Color( 0.7f, 0.7f, 0.7f, 1.0f );
Color k_aimColour_locked	= new Color( 1.0f, 1.0f, 1.0f, 1.0f );

const string FRP_LOW =	"<color=\"#ADADAD\">";
const string FRP_ERR =	"<color=\"#B84139\">";
const string FRP_WARN = "<color=\"#DEC521\">";
const string FRP_YES =	"<color=\"#69D128\">";
const string FRP_END =	"</color>";

#endregion

#region R_INSPECTOR

// Other behaviours
//[SerializeField]			ht8b_menu		menuController;
[SerializeField]			ht8b_cue[]		gripControllers;

// GameObjects
[SerializeField] public GameObject[]	balls_render;
[SerializeField] public GameObject		cuetip;
[SerializeField]			GameObject		guideline;
[SerializeField]			GameObject		guidefspin;
[SerializeField]			GameObject		devhit;
[SerializeField]			GameObject[]	playerTotems;
[SerializeField]			GameObject[]	cueTips;
[SerializeField]			GameObject		gametable;
[SerializeField]			GameObject		infBaseTransform;
[SerializeField]			GameObject		markerObj;
[SerializeField]			GameObject		infHowToStart;
[SerializeField]			GameObject		marker9ball;
[SerializeField]			GameObject		tableoverlayUI;
[SerializeField]			GameObject		fxColliderBase;
[SerializeField]			GameObject		pocketblockers;
[SerializeField]			GameObject		m_base;
[SerializeField]			GameObject		point4ball;
[SerializeField]			GameObject[]	cueRenderObjs;
[SerializeField]			GameObject		select4b;

// Meshes
[SerializeField]			Mesh[]			cueball_meshes;
[SerializeField]			Mesh				_9ball_mesh;
[SerializeField]			Mesh				_4ball_mesh_add;
[SerializeField]			Mesh				_4ball_mesh_minus;

// Texts
[SerializeField]			Text				ltext;
[SerializeField]			Text[]			playerNames;
[SerializeField]			Text				infText;
[SerializeField]			Text				infReset;

// Renderers
[SerializeField] public	Renderer			scoreCardRenderer;

// Materials
[SerializeField]			Material			guidelineMat;
[SerializeField] public Material			ballMaterial;
[SerializeField]			Material[]		CueGripMaterials;
[SerializeField] public Material			tableMaterial;
[SerializeField]			Material			markerMaterial;

[SerializeField] public Texture[]		textureSets;
[SerializeField]			Texture			scorecard8ball;
[SerializeField]			Texture			scorecard4ball;

// Audio
[SerializeField]			AudioClip		snd_Intro;
[SerializeField]			AudioClip		snd_Sink;
[SerializeField]			AudioClip[]		snd_Hits;
[SerializeField]			AudioClip		snd_NewTurn; 
[SerializeField]			AudioClip		snd_PointMade;
//[SerializeField]			AudioClip		snd_bad;
[SerializeField]			AudioClip		snd_btn;
[SerializeField]			AudioClip		snd_spin;
[SerializeField]			AudioClip		snd_spinstop;
[SerializeField]			AudioClip		snd_hitball;

//Reflection Probes
public ReflectionProbe Table_Probe;

#endregion

// Audio Components
AudioSource aud_main;

#region R_GAMESTATE

[UdonSynced]	private string netstr;		// dumpster fire
					private string netstr_prv;
					byte[]			net_data = new byte[0x52];

// Networked gamestate
//  data positions are marked as <#ushort>:<#bit> (<hexmask>) <description>

uint	sn_pocketed		= 0x00U;		// 18:0 (0xffff)	Each bit represents each ball, if it has been pocketed or not
public bool	sn_simulating	= false;		// 19:0 (0x01)		True whilst balls are rolling
uint	sn_turnid		= 0x00U;		// 19:1 (0x02)		Whos turn is it, 0 or 1
bool  sn_foul			= false;		// 19:2 (0x04)		End-of-turn foul marker
bool  sn_open			= true;		// 19:3 (0x08)		Is the table open?
uint  sn_playerxor	= 0x00;		// 19:4 (0x10)		What colour the players have chosen
bool  sn_gameover		= true;		// 19:5 (0x20)		Game is complete
uint  sn_winnerid		= 0x00U;		// 19:6 (0x40)		Who won the game if sn_gameover is set

bool	sn_lobbyclosed	= true;

[HideInInspector]
public bool	sn_permit= false;		// 19:7 (0x80)		Permission for player to play

// Modifiable -- ht8b_menu.cs

[HideInInspector] 

#if COMPILE_FUNC_TESTS
public
#endif

									uint sn_gamemode	= 0;	// 19:8 (0x700)	Gamemode ID 3 bit	{ 0: 8 ball, 1: 9 ball, 2+: undefined }
[HideInInspector] public	uint sn_timer		= 0;	// 19:13 (0x6000)	Timer ID 2 bit		{ 0: inf, 1: 30s, 2: 60s, 3: undefined }
									bool sn_teams = false;	// 19:15 (0x8000)	Teams on/off (1 bit)

ushort sn_packetid	= 0;			// 20:0 (0xffff)	Current packet number, used for locking updates so we dont accidently go back.
											//							this behaviour was observed on some long connections so its necessary
ushort sn_gameid		= 0;			// 21:0 (0xffff)	Game number
ushort sn_gmspec		= 0;			// 22:0 (0xffff)	Game mode specific information

// Cannot making a struct in C#, therefore values are duplicated

// for gamestate deltas
uint sn_pocketed_prv;
uint sn_turnid_prv;
bool sn_open_prv;
bool sn_gameover_prv;
ushort sn_gameid_prv;

uint sn_gamemode_prv;
uint sn_timer_prv;
bool sn_teams_prv;
bool sn_lobbyclosed_prv;

// unused
//bool sn_simulating_prv;
//bool sn_foul_prv;
//uint sn_playerxor_prv;
//uint sn_colourset_prv;
//bool sn_inmenu_prv;
//uint sn_winnerid_prv;
//bool sn_permit_prv;
//bool sn_rs_call8_prv;
//bool sn_rs_call_prv;
//bool sn_rs_anyf_prv;

// Local gamestates
[HideInInspector]
public bool	sn_armed	= false;		// Player is hitting
bool	sn_updatelock	= false;		// We are waiting for our local simulation to finish, before we unpack data
int	sn_firsthit		= 0;			// The first ball to be hit by cue ball

int	sn_secondhit	= 0;
int	sn_thirdhit		= 0;

bool	sn_oursim		= false;		// If the simulation was initiated by us, only set from update

byte	sn_wins0			= 0;			// Wins for player 0 (unused)
byte	sn_wins1			= 0;			// Wins for player 1 (unused)

float	introAminTimer = 0.0f;		// Ball dropper timer

bool	ballsMoving		= false;		// Tracker variable to see if balls are still on the go

bool	isReposition	= false;			// Repositioner is active
float repoMaxX			= k_TABLE_WIDTH;	// For clamping to table or set lower for kitchen

float timer_end		= 0.0f;		// What should the timer run out at
float timer_recip		= 0.0f;		// 1 over time limit
bool	timer_running	= false;

bool	particle_alive = false;
float	particle_time	= 0.0f;

bool	fb_madepoint	= false;
bool	fb_madefoul		= false;

bool	fb_jp				= false;
bool	fb_kr				= false;

int[]	fb_scores = new int[2];

bool	gm_is_0 = false;
bool	gm_is_1 = false;
bool	gm_is_2 = false;
//bool	gm_is_3 = false;
bool	gm_practice = false;			// Game should run in practice mode
bool	region_selected = false;

bool	dk_shootui = false;

// Values that will get sucked in from the menu
[HideInInspector] public int local_playerid = -1;
								uint local_teamid = 0u;		// Interpreted value

#endregion

#region R_PHYS_MEM

#if COMPILE_FUNC_TESTS
public 
#endif
Vector3[] ball_CO = new Vector3[16];	// Current positions

#if COMPILE_FUNC_TESTS
public 
#endif
Vector3[] ball_V = new Vector3[16];	// Current velocities
Vector3[] ball_W = new Vector3[16]; // Angular velocities

#endregion

// Shader uniforms
//  *udon currently does not support integer uniform identifiers
#if USE_INT_UNIFORMS

int uniform_tablecolour;
int uniform_scorecard_colour0;
int uniform_scorecard_colour1;
int uniform_scorecard_info;
int uniform_marker_colour;
int uniform_cue_colour;

#else

const string uniform_tablecolour = "_EmissionColour";
const string uniform_scorecard_colour0 = "_Colour0";
const string uniform_scorecard_colour1 = "_Colour1";
const string uniform_scorecard_info = "_Info";
const string uniform_marker_colour = "_Color";
const string uniform_cue_colour = "_ReColor";

#endif

#region R_VFX

Color tableSrcColour			= new Color( 1.0f, 1.0f, 1.0f, 1.0f );	// Runtime target colour
Color tableCurrentColour	= new Color( 1.0f, 1.0f, 1.0f, 1.0f );	// Runtime actual colour

// 'Pointer' colours.
Color pColour0;		// Team 0
Color pColour1;		// Team 1
Color pColour2;		// No team / open / 9 ball
Color pColourErr;
Color pClothColour;

// Updates table colour target to appropriate player colour
void _vis_apply_tablecolour( uint idsrc )
{
	if( gm_is_2 )
	{
		if( sn_turnid == 0 )
		{
			cueRenderObjs[ 0 ].GetComponent< MeshRenderer >().sharedMaterial.SetColor( uniform_cue_colour, pColour0 );
			cueRenderObjs[ 1 ].GetComponent< MeshRenderer >().sharedMaterial.SetColor( uniform_cue_colour, pColour1 * 0.5f );
		}
		else
		{
			cueRenderObjs[ 0 ].GetComponent< MeshRenderer >().sharedMaterial.SetColor( uniform_cue_colour, pColour0 * 0.5f );
			cueRenderObjs[ 1 ].GetComponent< MeshRenderer >().sharedMaterial.SetColor( uniform_cue_colour, pColour1 );
		}

		tableSrcColour = k_tableColourBlack;
	}
	
	else if( gm_is_1 )
	{
		cueRenderObjs[ sn_turnid ].GetComponent< MeshRenderer >().sharedMaterial.SetColor( uniform_cue_colour, k_tableColorWhite );
		cueRenderObjs[ sn_turnid ^ 0x1u ].GetComponent< MeshRenderer >().sharedMaterial.SetColor( uniform_cue_colour, k_tableColourBlack );

		tableSrcColour = pColour2;
	}

	else
	{
		if( !sn_open )
		{
			if( (idsrc ^ sn_playerxor) == 0 )
			{
				// Set table colour to blue
				tableSrcColour = pColour0;
			}
			else
			{
				// Table colour to orange
				tableSrcColour = pColour1;
			}

			cueRenderObjs[ sn_playerxor ].GetComponent< MeshRenderer >().sharedMaterial.SetColor( uniform_cue_colour, pColour0 );
			cueRenderObjs[ sn_playerxor ^ 0x1u ].GetComponent< MeshRenderer >().sharedMaterial.SetColor( uniform_cue_colour, pColour1 );
		}
		else
		{
			tableSrcColour = pColour2;

			cueRenderObjs[ sn_turnid ].GetComponent< MeshRenderer >().sharedMaterial.SetColor( uniform_cue_colour, k_tableColorWhite );
			cueRenderObjs[ sn_turnid ^ 0x1u ].GetComponent< MeshRenderer >().sharedMaterial.SetColor( uniform_cue_colour, k_tableColourBlack );
		}

	}

	CueGripMaterials[ sn_turnid ].SetColor( uniform_marker_colour, k_gripColourActive );
	CueGripMaterials[ sn_turnid ^ 0x1u ].SetColor( uniform_marker_colour, k_gripColourInactive );
}

void _vis_showballs()
{
	if( gm_is_1 )
	{
		for( int i = 0; i <= 9; i ++ )
			balls_render[ i ].SetActive( true );

		for( int i = 10; i < 16; i ++ )
			balls_render[ i ].SetActive( false );
	}
	else if( gm_is_2 )
	{
		for( int i = 1; i < 16; i ++ )
			balls_render[ i ].SetActive( false );

		balls_render[ 0 ].SetActive( true );
		balls_render[ 2 ].SetActive( true );
		balls_render[ 3 ].SetActive( true );
		balls_render[ 9 ].SetActive( true );
	}
	else
	{
		for( int i = 0; i < 16; i ++ )
		{
			balls_render[ i ].SetActive( true );
		}
	}
}

public void _vis_updatecoloursources()
{
	if( gm_is_1 )	// 9 Ball / USA colours
	{
		pColour0 = k_tableColourLBlue;
		pColour1 = k_tableColourLBlue;
		pColour2 = k_tableColourLBlue;

		pColourErr = k_tableColourBlack;	// No error effect
		pClothColour = k_fabricColour_blue;

		// 9 ball only uses one colourset / cloth colour
		ballMaterial.SetTexture( "_MainTex", textureSets[ 3 ] );
	}
	else if( gm_is_2 )
	{
		pColour0 = k_tableColorWhite;
		pColour1 = k_tableColourYellow;
		
		// Should not be used
		pColour2 = k_tableColourRed;
		pColourErr = k_tableColourRed;

		ballMaterial.SetTexture( "_MainTex", textureSets[ 2 ] );
		pClothColour = k_fabricColour_green;
	}
	else // Standard 8 ball derivatives
	{
		pColourErr = k_tableColourRed;
		pColour2 = k_tableColorWhite;

		pColour0 = k_tableColourBlue;
		pColour1 = k_tableColourOrange;

		ballMaterial.SetTexture( "_MainTex", textureSets[ 0 ] );
		pClothColour = k_fabricColour_gray;
	}
	tableMaterial.SetColor( "_ClothColour", pClothColour );
	Table_Probe.RenderProbe();
}

void _vis_disableobjects()
{
	guideline.SetActive( false );
	devhit.SetActive( false );
	infBaseTransform.SetActive( false );
	markerObj.SetActive( false );
	tableoverlayUI.SetActive( false );
	marker9ball.SetActive( false );
	point4ball.SetActive( false );
	select4b.SetActive( false );
}

void _vis_spawn_floaty( Vector3 pos, Mesh m )
{
	point4ball.SetActive( true );
	particle_alive = true;
	particle_time = 0.1f;

	// orient to be looking at player
	Vector3 lpos = Networking.LocalPlayer.GetPosition();
	Vector3 delta = lpos - this.transform.TransformPoint( pos );
	float r = Mathf.Atan2( delta.x, delta.z );
	point4ball.transform.localRotation = Quaternion.AngleAxis( r*Mathf.Rad2Deg, Vector3.up );

	// set position
	point4ball.transform.localPosition = pos;

	// Set scale
	point4ball.transform.localScale = Vector3.zero;

	point4ball.GetComponent< MeshFilter >().sharedMesh = m;
}

void _vis_floaty_eval()
{
	float scale,s,v,e;

	// Evaluate time
	particle_time += Time.deltaTime * 0.25f;

	// Sustained step
	s = Mathf.Max( particle_time-0.1f, 0.0f );
	v = Mathf.Min( particle_time*particle_time*100.0f, 21.0f*s*Mathf.Exp(-15.0f*s) );

	// Exponential step
	e = Mathf.Exp( -17.0f * Mathf.Pow( Mathf.Max( particle_time - 1.2f, 0.0f ), 3.0f ) );

	scale = e*v*2.0f;

	// Set scale
	point4ball.transform.localScale = new Vector3( scale, scale, scale );

	// Set position
	Vector3 temp = point4ball.transform.localPosition;
	temp.y = particle_time * 0.5f;
	point4ball.transform.localPosition = temp;

	// Particle death
	if( particle_time > 2.0f )
	{
		particle_alive = false;
		point4ball.SetActive( false );
	}
}

#endregion

void _timer_reset()
{
	if( sn_timer == 1 )	// 30s
	{
		timer_end = Time.timeSinceLevelLoad + 30.0f;
		timer_recip = 0.03333333333f;
	}
	else						// 60s
	{
		timer_end = Time.timeSinceLevelLoad + 60.0f;
		timer_recip = 0.01666666666f;
	}

	timer_running = true;
}

#region R_LOCALEV

void _onlocal_carompoint( Vector3 p )
{
	fb_madepoint = true;
	aud_main.PlayOneShot( snd_PointMade, 1.0f );

	fb_scores[ sn_turnid ] ++;

	if( fb_scores[ sn_turnid ] > 10 )
	{
		fb_scores[ sn_turnid ] = 10;
	}

	_vis_spawn_floaty( p, _4ball_mesh_add );
}

void _onlocal_carompenalize( Vector3 p )
{
	fb_madefoul = true;
	//aud_main.PlayOneShot( snd_bad, 1.0f );

	fb_scores[ sn_turnid ] --;

	if( fb_scores[ sn_turnid ] < 0 )
	{
		fb_scores[ sn_turnid ] = 0;
	}

	_vis_spawn_floaty( p, _4ball_mesh_minus );
}

// Called when a player first sinks a ball whilst the table was previously open
void _onlocal_tableclosed()
{
	uint picker = sn_turnid ^ sn_playerxor;

#if HT8B_DEBUGGER
	_frp( FRP_YES + "(local) " + Networking.GetOwner( playerTotems[ sn_turnid ] ).displayName + ":" + sn_turnid + " is " + 
		(picker == 0? "blues": "oranges") + FRP_END );
#endif

	_vis_apply_tablecolour( sn_turnid );
	_onlocal_updatescorecard();

	scoreCardRenderer.sharedMaterial.SetColor( uniform_scorecard_colour0, sn_playerxor == 0? pColour0: pColour1 );
	scoreCardRenderer.sharedMaterial.SetColor( uniform_scorecard_colour1, sn_playerxor == 1? pColour0: pColour1 );
}

// End of the game. Both with/loss
void _onlocal_gameover()
{
#if HT8B_DEBUGGER
	_frp( FRP_YES + "(local) Winner of match: " + Networking.GetOwner( playerTotems[ sn_winnerid ] ).displayName + FRP_END );
#endif

	_vis_apply_tablecolour( sn_winnerid );

	infText.text = Networking.GetOwner( playerTotems[ sn_winnerid ] ).displayName + " wins!";
	infBaseTransform.SetActive( true );
	marker9ball.SetActive( false );
	tableoverlayUI.SetActive( false );

#if !HT_QUEST
	_vis_rackballs();	// To make sure rigidbodies are completely off
#endif

	isReposition = false;
	markerObj.SetActive( false );

	_onlocal_updatescorecard();

	// Remove any access rights
	local_playerid = -1;
	_apply_cue_access();

	_htmenu_enter();
}

void _onlocal_turnchange()
{
	// Effects
	_vis_apply_tablecolour( sn_turnid );
	aud_main.PlayOneShot( snd_NewTurn, 1.0f );

	// Register correct cuetip
	cuetip = cueTips[ sn_turnid ];

	bool isOurTurn = ((local_playerid >= 0) && (local_teamid == sn_turnid)) || gm_practice;

	if( gm_is_2 ) // 4 ball
	{
		// Swap cue ball and opponent cue
		Vector3 temp = ball_CO[ 0 ];
		ball_CO[ 0 ] = ball_CO[ 9 ];
		ball_CO[ 9 ] = temp;

		if( sn_turnid == 0 )
		{
			balls_render[ 0 ].GetComponent<MeshFilter>().sharedMesh = cueball_meshes[ 0 ];
			balls_render[ 9 ].GetComponent<MeshFilter>().sharedMesh = cueball_meshes[ 1 ];
		}
		else
		{
			balls_render[ 9 ].GetComponent<MeshFilter>().sharedMesh = cueball_meshes[ 0 ];
			balls_render[ 0 ].GetComponent<MeshFilter>().sharedMesh = cueball_meshes[ 1 ];
		}
	}
	else
	{
		// White was pocketed
		if( (sn_pocketed & 0x1u) == 0x1u )
		{
			ball_CO[0] = Vector3.zero;
			ball_V[0] = Vector3.zero;
			ball_W[0] = Vector3.zero;

			sn_pocketed &= 0xFFFFFFFEu;
		}
	}

	if( isOurTurn )
	{
		if( sn_foul )
		{
			isReposition = true;
			repoMaxX = k_TABLE_WIDTH;
			markerObj.SetActive( true );

			markerObj.transform.localPosition = ball_CO[ 0 ];
		}
	}

	// Force timer reset
	if( sn_timer > 0 )
	{
		_timer_reset();
	}
}

void _onlocal_updatescorecard()
{
	if( gm_is_2 )
	{
		scoreCardRenderer.sharedMaterial.SetVector( uniform_scorecard_info, new Vector4( fb_scores[ 0 ]*0.04681905f, fb_scores[ 1 ]*0.04681905f, 0.0f, 0.0f ) );
	}
	else
	{
		int[] counter0 = new int[2];

		uint temp = sn_pocketed;

		for( int j = 0; j < 2; j ++ )
		{
			for( int i = 0; i < 7; i ++ )
			{
				if( (temp & 0x4) > 0 )
				{
					counter0[ j ^ sn_playerxor ] ++;
				}

				temp >>= 1;
			}
		}

		// Add black ball if we are winning the thing
		if( sn_gameover )
		{
			counter0[ sn_winnerid ] += (int)((sn_pocketed & 0x2) >> 1);
		}

		scoreCardRenderer.sharedMaterial.SetVector( uniform_scorecard_info, new Vector4( counter0[0]*0.0625f, counter0[1]*0.0625f, 0.0f, 0.0f ) );
	}
}

// Player scored an objective ball 
void _onlocal_pocket_obj()
{
	// Make a bright flash
	tableCurrentColour *= 1.9f;

	aud_main.PlayOneShot( snd_Sink, 1.0f );
}

// Player scored a foul ball (cue, non-objective or 8 before set cleared)
void _onlocal_pocket_enm()
{
	tableCurrentColour = pColourErr;

	aud_main.PlayOneShot( snd_Sink, 1.0f );
}

// once balls stops rolling this is called
void _onlocal_sim_end()
{
	sn_simulating = false;

	#if HT8B_DEBUGGER
	_frp( FRP_LOW + "(local) SimEnd()" + FRP_END );
	#endif

	// Make sure we only run this from the client who initiated the move
	if( sn_oursim )
	{
		sn_oursim = false;

		// We are updating the game state so make sure we are network owner
		Networking.SetOwner( Networking.LocalPlayer, this.gameObject );

		// Owner state checks
		#if HT8B_DEBUGGER
		_frp( FRP_LOW + "Post-move state checking" + FRP_END );
		#endif

		uint bmask = 0xFFFCu;
		uint emask = 0x0u;

		// Quash down the mask if table has closed
		if( !sn_open )
		{
			bmask = bmask & (0x1FCu << ((int)(sn_playerxor ^ sn_turnid) * 7));
			emask = 0x1FCu << ((int)(sn_playerxor ^ sn_turnid ^ 0x1U) * 7);
		}

		// Common informations
		bool isSetComplete = (sn_pocketed & bmask) == bmask;
		bool isScratch = (sn_pocketed & 0x1U) == 0x1U;

		// Append black to mask if set is done
		if( isSetComplete )
		{
			bmask |= 0x2U;
		}

		// These are the resultant states we can set for each mode
		// then the rest is taken care of
		bool 
			isObjectiveSink,
			isOpponentSink,
			winCondition,
			foulCondition,
			deferLossCondition
		;

		if( gm_is_0 )	// Standard 8 ball
		{
			isObjectiveSink = (sn_pocketed & bmask) > (sn_pocketed_prv & bmask);
			isOpponentSink = (sn_pocketed & emask) > (sn_pocketed_prv & emask);

			// Calculate if objective was not hit first
			bool isWrongHit = ((0x1U << sn_firsthit) & bmask) == 0;

			bool is8Sink = (sn_pocketed & 0x2U) == 0x2U;

			winCondition = isSetComplete && is8Sink;
			foulCondition = isScratch || isWrongHit;
			
			deferLossCondition = is8Sink;
		} 
		else if( gm_is_1 )	// 9 ball
		{
			// Rules are from: https://www.youtube.com/watch?v=U0SbHOXCtFw

			// Rule #1: Cueball must strike the lowest number ball, first
			bool isWrongHit = !(_lowest_ball( sn_pocketed_prv ) == sn_firsthit);

			// Rule #2: Pocketing cueball, is a foul
			
			// Win condition: Pocket 9 ball ( at anytime )
			winCondition = (sn_pocketed & 0x200u) == 0x200u;

			// this video is hard to follow so im just gonna guess this is right
			isObjectiveSink = (sn_pocketed & 0x3FEu) > (sn_pocketed_prv & 0x3FEu);
			
			isOpponentSink = false;
			deferLossCondition = false;

			foulCondition = isWrongHit || isScratch;

			// TODO: Implement rail contact requirement
		} 
		else if( gm_is_2 ) // 4 ball
		{
			isObjectiveSink = fb_madepoint;
			isOpponentSink = fb_madefoul;
			foulCondition = false;
			deferLossCondition = false;

			winCondition = fb_scores[ sn_turnid ] >= 10;
		}
		else // Unkown mode
		{
			isObjectiveSink = true;
			isOpponentSink = false;
			winCondition = false;
			foulCondition = false;
			deferLossCondition = false;

			if( (sn_pocketed & 0x1u) == 0x1u )
			{
				sn_foul = true;
				_onlocal_turnchange();
			}
		}

		if( winCondition )
		{
			if( foulCondition )
			{
				// Loss
				_turn_win( sn_turnid ^ 0x1U );
			}
			else
			{
				// Win
				_turn_win( sn_turnid );
			}
		}
		else if( deferLossCondition )
		{
			// Loss
			_turn_win( sn_turnid ^ 0x1U );
		}
		else if( foulCondition )
		{
			// Foul
			_turn_foul();
		}
		else if( isObjectiveSink && !isOpponentSink )
		{
			// Continue
			_turn_continue();
		}
		else
		{
			// Pass
			_turn_pass();
		}
	}

	// Check if there was a network update on hold
	if( sn_updatelock )
	{
		#if HT8B_DEBUGGER
		_frp( FRP_LOW + "Update was waiting, executing now" + FRP_END );
		#endif

		sn_updatelock = false;

		_netread();
	}
}

void _onlocal_timer_end()
{
	timer_running = false;
	
	#if HT8B_DEBUGGER
	_frp( FRP_ERR + "Out of time!!" + FRP_END );
	#endif

	// We are holding the stick so propogate the change
	if( Networking.GetOwner( playerTotems[ sn_turnid ] ) == Networking.LocalPlayer )
	{
		Networking.SetOwner( Networking.LocalPlayer, this.gameObject );
		_turn_foul();
	}
	else
	{
		// All local players freeze until next target
		// can pick up and propogate timer end
		sn_permit = false;
	}
}


// Grant cue access if we are playing
void _apply_cue_access()
{
	if( local_playerid >= 0 )
	{
		if( gm_practice )
		{
			gripControllers[ 0 ]._access_allow();
			gripControllers[ 1 ]._access_allow();
		}
		else
		{
			if( (local_teamid & 0x1) > 0 )						// Local player is 1, or 3
			{
				gripControllers[ 1 ]._access_allow();
				gripControllers[ 0 ]._access_deny();
			}
			else															// Local player is 0, or 2
			{
				gripControllers[ 0 ]._access_allow();
				gripControllers[ 1 ]._access_deny();
			}
		}
	}
	else
	{
		gripControllers[ 0 ]._access_deny();
		gripControllers[ 1 ]._access_deny();
	}
}

// Some udon specific optimisations
void _setup_gm_spec()
{
	gm_is_0 = sn_gamemode == 0u;
	gm_is_1 = sn_gamemode == 1u;
	gm_is_2 = sn_gamemode == 2u;
}

void _onlocal_newgame()
{
	#if HT8B_DEBUGGER
	_frp( FRP_LOW + "NewGameLocal()" + FRP_END );
	#endif

	_setup_gm_spec();

	// Calculate interpreted values from menu states
	if( local_playerid >= 0 )
		local_teamid = (uint)local_playerid & 0x1u;

	// Disable menu
	_htmenu_exit();

	// Reflect menu-state settings (for late joiners)
	_vis_updatecoloursources();
	_vis_apply_tablecolour(0);
	_apply_cue_access();

	// TODO: move to function
	if( gm_is_1 )	// 9 ball specific
	{
		scoreCardRenderer.gameObject.SetActive( false );
		marker9ball.SetActive( true );
	}
	else
	{
		scoreCardRenderer.gameObject.SetActive( true );
		marker9ball.SetActive( false );
	}

	if( gm_is_2 ) // 4 ball specific
	{
		pocketblockers.SetActive( true );
		scoreCardRenderer.sharedMaterial.SetTexture( "_MainTex", scorecard4ball );

		scoreCardRenderer.sharedMaterial.SetColor( uniform_scorecard_colour0, pColour0 );
		scoreCardRenderer.sharedMaterial.SetColor( uniform_scorecard_colour1, pColour1 );

		fb_scores[ 0 ] = 0;
		fb_scores[ 1 ] = 0;

		// Reset mesh filters on balls that change them
		balls_render[ 0 ].GetComponent<MeshFilter>().sharedMesh = cueball_meshes[ 0 ];
		balls_render[ 9 ].GetComponent<MeshFilter>().sharedMesh = cueball_meshes[ 1 ];
	}
	else
	{
		pocketblockers.SetActive( false );
		scoreCardRenderer.sharedMaterial.SetTexture( "_MainTex", scorecard8ball );

		// Reset mesh filters on balls that change them
		balls_render[ 0 ].GetComponent<MeshFilter>().sharedMesh = cueball_meshes[ 0 ];
		balls_render[ 9 ].GetComponent<MeshFilter>().sharedMesh = _9ball_mesh;
	}


	_vis_showballs();

	// Reflect game state
	_onlocal_updatescorecard();
	isReposition = false;
	markerObj.SetActive( false );
	infBaseTransform.SetActive( false );

	// Effects
	introAminTimer = 2.0f;
	aud_main.PlayOneShot( snd_Intro, 1.0f );

	// Player name texts
	string base_text = "";
	if( sn_teams )
	{
		base_text = "Team ";
	}

	tableoverlayUI.SetActive( true );
	playerNames[0].text = base_text + Networking.GetOwner( playerTotems[0] ).displayName;
	playerNames[1].text = base_text + Networking.GetOwner( playerTotems[1] ).displayName;

	timer_running = false;

	// Switch desktop/vr
	bool usr_desktop = !Networking.LocalPlayer.IsUserInVR();

	#if !HT_QUEST
	gripControllers[0].useDesktop = usr_desktop;
	gripControllers[1].useDesktop = usr_desktop;
	#endif
}

#endregion

#region R_PHYS

// Cue input tracking

Vector3	cue_lpos;
Vector3	cue_llpos;
Vector3	cue_vdir;
Vector3	cue_shotdir;
float		cue_fdir;

#if HT_QUEST
#else
public Vector3	dkTargetPos;				// Target for desktop aiming
#endif

#if !HT_QUEST

// Finalize positions onto their rack spots
void _vis_rackballs()
{
	uint ball_bit = 0x1u;

	for( int i = 0; i < 16; i ++ )
	{
		balls_render[ i ].GetComponent< Rigidbody >().isKinematic = true;
		
		if( (ball_bit & sn_pocketed) == ball_bit )
		{
			balls_render[ i ].transform.localPosition = new Vector3(
				ball_CO[ i ].x,
				k_RACK_HEIGHT,
				ball_CO[ i ].z
			);
		}

		ball_bit <<= 1;
	}
}

#endif

// Internal game state pocket and enable unity physics to play out the rest
void _onlocal_pocketball( int id )
{
	uint total = 0U;

	// Get total for X positioning
	int count_extent = gm_is_1? 10: 16;
	for( int i = 1; i < count_extent; i ++ )
	{
		total += (sn_pocketed >> i) & 0x1U;
	}

	// set this for later
	ball_CO[ id ].x = -0.9847f + (float)total * k_BALL_DIAMETRE;
	ball_CO[ id ].z = 0.768f;
	
	sn_pocketed ^= 1U << id;

	uint bmask = 0x1FCU << ((int)(sn_turnid ^ sn_playerxor) * 7);

	// Good pocket
	if( ((0x1U << id) & ((bmask) | (sn_open ? 0xFFFCU: 0x0000U) | ((bmask & sn_pocketed) == bmask? 0x2U: 0x0U))) > 0 )
	{
		_onlocal_pocket_obj();
	}
	else
	{
		// bad
		_onlocal_pocket_enm();
	}

	#if !HT_QUEST

	// VFX ( make ball move )
	Rigidbody body = balls_render[ id ].GetComponent< Rigidbody >();
	body.isKinematic = false;
	body.velocity = new Vector3(
	
		ball_V[ id ].x,
		0.0f,
		ball_V[ id ].z
		
	);

	#else

	balls_render[ id ].transform.position = new Vector3(

		ball_CO[ id ].x,
		k_RACK_HEIGHT,
		ball_CO[ id ].y
	
	);
	#endif
}

// Makes sure that velocity is not opposing surface normal
/*
void _clamp_ball_vel_semi( int id, Vector2 surface )
{
	// TODO: improve this method to be a bit more accurate
	if( Vector2.Dot( ball_V[ id ], surface ) < 0.0f )
	{
		ball_V[ id ] = ball_V[ id ].magnitude * surface;
	}
}
*/

// Is cue touching another ball?
bool _cue_contacting()
{
	// 8 ball, practice, portal
	if( sn_gamemode != 1u )
	{
		// Check all
		for( int i = 1; i < 16; i ++ )
		{
			if( (ball_CO[ 0 ] - ball_CO[ i ]).sqrMagnitude < k_BALL_DSQR )
			{
				return true;
			}
		}
	}
	else // 9 ball
	{
		// Only check to 9 ball
		for( int i = 1; i <= 9; i ++ )
		{
			if( (ball_CO[ 0 ] - ball_CO[ i ]).sqrMagnitude < k_BALL_DSQR )
			{
				return true;
			}
		}
	}

	return false;
}

// Check pocket condition
void _phy_ball_pockets( int id )
{
	float zz, zx;
	Vector3 A;

	A = ball_CO[ id ];

	// Setup major regions
	zx = Mathf.Sign( A.x );
	zz = Mathf.Sign( A.z );

	// Its in a pocket
	if( A.z*zz > k_TABLE_HEIGHT + k_POCKET_DEPTH || A.z*zz > A.x*-zx + k_TABLE_WIDTH+k_TABLE_HEIGHT + k_POCKET_DEPTH )
	{
		_onlocal_pocketball( id );
	}
}

const float k_SINA = 0.28078832987f;
const float k_SINA2 = 0.07884208619f;
const float k_COSA = 0.95976971915f;
const float k_COSA2 = 0.92115791379f;
const float k_EP1 = 1.79f;
const float k_A = 21.875f;
const float k_B = 6.25f;
const float k_F = 1.72909790282f;

// Apply cushion bounce
void _phy_bounce_cushion( int id, Vector3 N )
{
	// Mathematical expressions derived from: https://billiards.colostate.edu/physics_articles/Mathavan_IMechE_2010.pdf
	//
	// (Note): subscript gamma, u, are used in replacement of Y and Z in these expressions because
	// unicode does not have them.
	//
	// f = 2/7
	// f₁ = 5/7
	// 
	// Velocity delta:
	//   Δvₓ = −vₓ∙( f∙sin²θ + (1+e)∙cos²θ ) − Rωᵤ∙sinθ
	//   Δvᵧ = 0
	//   Δvᵤ = f₁∙vᵤ + fR( ωₓ∙sinθ - ωᵧ∙cosθ ) - vᵤ
	//
	// Aux:
	//   Sₓ = vₓ∙sinθ - vᵧ∙cosθ+ωᵤ
	//   Sᵧ = 0
	//   Sᵤ = -vᵤ - ωᵧ∙cosθ + ωₓ∙cosθ
	//   
	//   k = (5∙Sᵤ) / ( 2∙mRA ); 
	//   c = vₓ∙cosθ - vᵧ∙cosθ
	//
	// Angular delta:
	//   ωₓ = k∙sinθ
	//   ωᵧ = k∙cosθ
	//   ωᵤ = (5/(2m))∙(-Sₓ / A + ((sinθ∙c∙(e+1)) / B)∙(cosθ - sinθ));
	//
	// These expressions are in the reference frame of the cushion, so V and ω inputs need to be rotated

	// Reject bounce if velocity is going the same way as normal
	// this state means we tunneled, but it happens only on the corner
	// vertexes
	Vector3 source_v = ball_V[ id ];
	if( Vector3.Dot( source_v, N ) > 0.0f )
	{
		return;
	}

	// Rotate V, W to be in the reference frame of cushion
	Quaternion rq = Quaternion.AngleAxis( Mathf.Atan2( -N.z, -N.x ) * Mathf.Rad2Deg, Vector3.up );
	Quaternion rb = Quaternion.Inverse( rq );
	Vector3 V = rq * source_v;
	Vector3 W = rq * ball_W[ id ];
	 
	Vector3 V1; 
	Vector3 W1;
	float k, c, s_x, s_z;

	//V1.x = -V.x * ((2.0f/7.0f) * k_SINA2 + k_EP1 * k_COSA2) - (2.0f/7.0f) * k_BALL_PL_X * W.z * k_SINA;
	//V1.z = (5.0f/7.0f)*V.z + (2.0f/7.0f) * k_BALL_PL_X * (W.x * k_SINA - W.y * k_COSA) - V.z;
	//V1.y = 0.0f; 
	// (baked):
	V1.x = -V.x*k_F - 0.00240675711f*W.z;
	V1.z = 0.71428571428f*V.z + 0.00857142857f*(W.x*k_SINA-W.y*k_COSA) - V.z;
	V1.y = 0.0f;

	// s_x = V.x * k_SINA - V.y * k_COSA + W.z;
	// (baked): y component not used:
	s_x = V.x * k_SINA + W.z;
	s_z = -V.z - W.y * k_COSA + W.x * k_SINA; 

	// k = (5.0f * s_z) / ( 2 * k_BALL_MASS * k_A ); 
	// (baked):
	k = s_z * 0.71428571428f; 

	// c = V.x * k_COSA - V.y * k_COSA;
	// (baked): y component not used
	c = V.x * k_COSA;

	W1.x = k * k_SINA;

	//W1.z = (5.0f / (2.0f * k_BALL_MASS)) * (-s_x / k_A + ((k_SINA * c * k_EP1) / k_B) * (k_COSA - k_SINA));
	// (baked):
	W1.z = 15.625f * (-s_x * 0.04571428571f + c * 0.0546021744f);
	W1.y = k * k_COSA;

	// Unrotate result
	ball_V[ id ] += rb * V1;
	ball_W[ id ] += rb * W1;
}

// Pocketless table
void _phy_ball_table_carom( int id )
{
	float zz, zx;
	Vector3 A;

	A = ball_CO[ id ];

	// Setup major regions
	zx = Mathf.Sign( A.x );
	zz = Mathf.Sign( A.z );

	if( A.x * zx > k_TABLE_WIDTH )
	{
		ball_CO[ id ].x = k_TABLE_WIDTH * zx;
		_phy_bounce_cushion( id, Vector3.left * zx );
	}

	if( A.z * zz > k_TABLE_HEIGHT )
	{
		ball_CO[ id ].z = k_TABLE_HEIGHT * zz;
		_phy_bounce_cushion( id, Vector3.back * zz );
	}
}

// TODO: inline this
void _phy_ball_table_std( int id )
{
	float zy, zx, zk, zw, d, k, i, j, l, r;
	Vector3 A, N;

	A = ball_CO[ id ];

	// REGIONS
	/*  
		*  QUADS:							SUBSECTION:				SUBSECTION:
		*    zx, zy:							zz:						zw:
		*																
		*  o----o----o  +:  1			\_________/				\_________/
		*  | -+ | ++ |  -: -1		     |	    /		              /  /
		*  |----+----|					  -  |  +   |		      -     /   |
		*  | -- | +- |						  |	   |		          /  +  |
		*  o----o----o						  |      |             /       |
		* 
		*/

	// Setup major regions
	zx = Mathf.Sign( A.x );
	zy = Mathf.Sign( A.z );

	// within pocket regions
	if( (A.z*zy > (k_TABLE_HEIGHT-k_POCKET_RADIUS)) && (A.x*zx > (k_TABLE_WIDTH-k_POCKET_RADIUS) || A.x*zx < k_POCKET_RADIUS) )
	{
		// Subregions
		zw = A.z * zy > A.x * zx - k_TABLE_WIDTH + k_TABLE_HEIGHT ? 1.0f : -1.0f;

		// Normalization / line coefficients change depending on sub-region
		if( A.x * zx > k_TABLE_WIDTH * 0.5f )
		{
			zk = 1.0f;
			r = k_1OR2;
		}
		else
		{
			zk = -2.0f;
			r = k_1OR5;
		}

		// Collider line EQ
		d = zx * zy * zk; // Coefficient
		k = (-(k_TABLE_WIDTH * Mathf.Max(zk, 0.0f)) + k_POCKET_RADIUS * zw * Mathf.Abs( zk ) + k_TABLE_HEIGHT) * zy; // Konstant

		// Check if colliding
		l = zw * zy;
		if( A.z * l > (A.x * d + k) * l )
		{
			// Get line normal
			N.x = zx * zk;
			N.z = -zy;
			N.y = 0.0f;
			N *= zw * r;

			// New position
			i = (A.x * d + A.z - k) / (2.0f * d);
			j = i * d + k;

			ball_CO[ id ].x = i;
			ball_CO[ id ].z = j;

			// Reflect velocity
			_phy_bounce_cushion( id, N );
		}
	}
	else // edges
	{
		if( A.x * zx > k_TABLE_WIDTH )
		{
			ball_CO[ id ].x = k_TABLE_WIDTH * zx;
			_phy_bounce_cushion( id, Vector3.left * zx );
		}

		if( A.z * zy > k_TABLE_HEIGHT )
		{
			ball_CO[ id ].z = k_TABLE_HEIGHT * zy;
			_phy_bounce_cushion( id, Vector3.back * zy );
		}
	}
}

// Advance simulation 1 step for ball id
void _phy_ball_step( int id )
{
	// Since v1.5.0
	Vector3 V = ball_V[ id ];
	Vector3 W = ball_W[ id ];
	Vector3 cv;

	// Equations derived from: http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.89.4627&rep=rep1&type=pdf
	// 
	// R: Contact location with ball and floor aka: (0,-r,0)
	// µₛ: Slipping friction coefficient
	// µᵣ: Rolling friction coefficient
	// i: Up vector aka: (0,1,0)
	// g: Planet Earth's gravitation acceleration ( 9.80665 )
	// 
	// Relative contact velocity (marlow):
	//   c = v + R✕ω
	//
	// Ball is classified as 'rolling' or 'slipping'. Rolling is when the relative velocity is none and the ball is
	// said to be in pure rolling motion
	//
	// When ball is classified as rolling:
	//   Δv = -µᵣ∙g∙Δt∙(v/|v|)
	//
	// Angular momentum can therefore be derived as:
	//   ωₓ = -vᵤ/R
	//   ωᵧ =  0
	//   ωᵤ =  vₓ/R
	//
	// In the slipping state:
	//   Δω = ((-5∙µₛ∙g)/(2/R))∙Δt∙i✕(c/|c|)
	//   Δv = -µₛ∙g∙Δt(c/|c|)

	// Relative contact velocity of ball and table
	cv = V + Vector3.Cross( k_CONTACT_POINT, W );
	
	// Rolling is achieved when cv's length is approaching 0
	// The epsilon is quite high here because of the fairly large timestep we are working with
	if( cv.magnitude <= 0.1f )
	{
		//V += -k_F_ROLL * k_GRAVITY * k_FIXED_TIME_STEP * V.normalized;
		// (baked):
		V += -0.00122583125f * V.normalized;

		// Calculate rolling angular velocity
		W.x = -V.z * k_BALL_1OR;
		W.y =  0.0f;
		W.z =  V.x * k_BALL_1OR;

		// Stopping scenario
		if( V.magnitude < 0.01f && W.magnitude < 0.2f )
		{
			W = Vector3.zero;
			V = Vector3.zero;
		}
		else
		{
			ballsMoving = true;
		}
	}
	else // Slipping
	{
		Vector3 nv = cv.normalized;

		// Angular slipping friction
		//W += ((-5.0f * k_F_SLIDE * 9.8f)/(2.0f * 0.03f)) * k_FIXED_TIME_STEP * Vector3.Cross( Vector3.up, nv );
		// (baked):
		W += -2.04305208f * Vector3.Cross( Vector3.up, nv );
		V += -k_F_SLIDE * 9.8f * k_FIXED_TIME_STEP * nv;

		ballsMoving = true;
	}

	ball_W[ id ] = W;
	ball_V[ id ] = V;
	balls_render[ id ].transform.Rotate( W.normalized, W.magnitude * k_FIXED_TIME_STEP * -Mathf.Rad2Deg, Space.World );

	uint ball_bit = 0x1U << id;

	// ball/ball collisions
	for( int i = id+1; i < 16; i ++ )
	{
		ball_bit <<= 1;

		if( (ball_bit & sn_pocketed) != 0U )
			continue;

		Vector3 delta = ball_CO[ i ] - ball_CO[ id ];
		float dist = delta.magnitude;

		if( dist < k_BALL_DIAMETRE )
		{
			// Physics shit

			Vector3 normal = delta / dist;

			Vector3 velocityDelta = ball_V[ id ] - ball_V[ i ];

			float dot = Vector3.Dot( velocityDelta, normal );

			if( dot > 0.0f ) 
			{
				Vector3 reflection = normal * dot;
				ball_V[id] -= reflection;
				ball_V[i] += reflection;

				// Prevent sound spam if it happens
				if( ball_V[ id ].sqrMagnitude > 0 && ball_V[ i ].sqrMagnitude > 0 )
				{
					int clip = UnityEngine.Random.Range(0, snd_Hits.Length - 1);
					float vol = Mathf.Clamp01((ball_V[id].magnitude + ball_V[i].magnitude) * reflection.magnitude);
					AudioSource.PlayClipAtPoint(snd_Hits[clip], balls_render[id].transform.position, vol);
				}

				// First hit detected
				if( id == 0 )
				{
					if( gm_is_2 )
					{
						if( fb_kr )	// KR 사구 ( Sagu )
						{
							if( i == 9 )
							{
								if( !fb_madefoul )
								{
									_onlocal_carompenalize( ball_CO[ i ] );
								}
							}
							else
							{
								if( sn_firsthit == 0 )
								{
									sn_firsthit = i;
								}
								else
								{
									if( i != sn_firsthit )
									{
										if( sn_secondhit == 0 )
										{
											sn_secondhit = i;
											_onlocal_carompoint( ball_CO[ i ] );
										}
									}
								}
							}
						}
						else // JP 四つ玉 ( Yotsudama )
						{
							if( sn_firsthit == 0 )
							{
								sn_firsthit = i;
							}
							else
							{
								if( sn_secondhit == 0 )	
								{
									if( i != sn_firsthit )
									{
										sn_secondhit = i;
										_onlocal_carompoint( ball_CO[ i ] );
									}
								}
								else 
								{
									if( sn_thirdhit == 0 )
									{
										if( i != sn_firsthit && i != sn_secondhit )
										{
											sn_thirdhit = i;
											_onlocal_carompoint( ball_CO[ i ] );
										}
									}
								}
							}
						}
					}
					else
					{
						if( sn_firsthit == 0 )
						{
							sn_firsthit = i;
						}
					}
				}
			}
		}
	}
}

// ( Since v0.2.0a ) Check if we can predict a collision before move update happens to improve accuracy
bool _phy_predict_cueball()
{
	// Get what will be the next position
	Vector3 originalDelta = ball_V[ 0 ] * k_FIXED_TIME_STEP;
	Vector3 norm = ball_V[ 0 ].normalized;
	
	Vector3 h;
	float lf, s, nmag;

	// Closest found values
	float minlf = 9999999.0f;
	int minid = 0;
	float mins = 0;

	uint ball_bit = 0x1U;

	// Loop balls look for collisions
	for( int i = 1; i < 16; i ++ )
	{
		ball_bit <<= 1;

		if( (ball_bit & sn_pocketed) != 0U )
			continue;

		h = ball_CO[ i ] - ball_CO[ 0 ];
		lf = Vector3.Dot( norm, h );
		s = k_BALL_DSQRPE - Vector3.Dot( h, h ) + lf * lf;

		if( s < 0.0f )
			continue;

		if( lf < minlf )
		{
			minlf = lf;
			minid = i;
			mins = s;
		}
	}

	if( minid > 0 )
	{
		nmag = minlf-Mathf.Sqrt( mins );

		// Assign new position if got appropriate magnitude
		if( nmag * nmag < originalDelta.sqrMagnitude )
		{
			ball_CO[ 0 ] += norm * nmag;
			return true;
		}
	}

	return false;
}

// Run one physics iteration for all balls
void _phys_step()
{
#if !COMPILE_FUNC_TESTS
	ballsMoving = false;
#else
	ballsMoving = true;
#endif

	uint ball_bit = 0x1u;

	// Cue angular velocity
	if( (sn_pocketed & 0x1U) == 0 )
	{

		if( !_phy_predict_cueball() )
		{
			// Apply movement
			ball_CO[ 0 ] += ball_V[ 0 ] * k_FIXED_TIME_STEP;
		}

		_phy_ball_step( 0 );
	}

	// Run main simulation / inter-ball collision
	for( int i = 1; i < 16; i ++ )
	{
		ball_bit <<= 1;

		if( (ball_bit & sn_pocketed) == 0U )
		{
			ball_CO[ i ] += ball_V[ i ] * k_FIXED_TIME_STEP;
			
			_phy_ball_step( i );
		}
	}

	// Check if simulation has settled
	if( !ballsMoving )
	{
		if( sn_simulating )
		{
			_onlocal_sim_end();
		}

		return;
	}

	if( gm_is_2 )
	{
		_phy_ball_table_carom( 0 );
		_phy_ball_table_carom( 2 );
		_phy_ball_table_carom( 3 );
		_phy_ball_table_carom( 9 );
	}
	else
	{
		ball_bit = 0x1U;

		// Run edge collision
		for( int i = 0; i < 16; i ++ )
		{
			if( (ball_bit & sn_pocketed ) == 0U )
				_phy_ball_table_std( i );
		
			ball_bit <<= 1;
		}
	}

	if( gm_is_2 ) return;

	ball_bit = 0x1U;

	// Run triggers
	for( int i = 0; i < 16; i ++ )
	{
		if( (ball_bit & sn_pocketed ) == 0U )
		{
			_phy_ball_pockets( i );
		}

		ball_bit <<= 1;
	}
}

// Ray circle intersection
// yes, its fixed size circle
// Output is dispensed into the below variable
// One intersection point only
// This is not used in physics calcuations, only cue input

Vector2 RayCircle_output;
bool _phy_ray_circle( Vector2 start, Vector2 dir, Vector2 circle )
{
	Vector2 nrm = dir.normalized;
	Vector2 h = circle - start;
	float lf = Vector2.Dot( nrm, h );
	float s = k_BALL_RSQR - Vector2.Dot( h, h ) + lf * lf;

	if( s < 0.0f ) return false;

	s = Mathf.Sqrt( s );

	if( lf < s )
	{
		if( lf + s >= 0 )
		{
			s = -s;
		}
		else
		{
			return false;
		}
	}

	RayCircle_output = start + nrm * (lf-s);
	return true;
}

Vector3 RaySphere_output;
bool _phy_ray_sphere( Vector3 start, Vector3 dir, Vector3 sphere )
{
	Vector3 nrm = dir.normalized;
	Vector3 h = sphere - start;
	float lf = Vector3.Dot( nrm, h );
	float s = k_BALL_RSQR - Vector3.Dot(h, h) + lf * lf;

	if( s < 0.0f ) return false;

	s = Mathf.Sqrt( s );

	if( lf < s )
	{
		if( lf + s >= 0 )
		{
			s = -s;
		}
		else
		{
			return false;
		}
	}

	RaySphere_output = start + nrm * (lf-s);
	return true;
}

// Closest point on line from pos
Vector2 _line_project( Vector2 start, Vector2 dir, Vector2 pos )
{
	return start + dir * Vector2.Dot( pos - start, dir );
}
#endregion

#region R_UTIL

// Find the lowest numbered ball, that isnt the cue, on the table
// This function finds the VISUALLY represented lowest ball,
// since 8 has id 1, the search needs to be split
int _lowest_ball( uint field )
{
	for( int i = 2; i <= 8; i ++ )
	{
		if( ((field >> i) & 0x1U) == 0x00U )
			return i;
	}

	if( ((field) & 0x2U) == 0x00U )
		return 1;

	for( int i = 9; i < 16; i ++ )
	{
		if( ((field >> i) & 0x1U) == 0x00U )
			return i;
	}

	// ??
	return 0;
}

#endregion

#region R_TURNRULES

void _turn_win( uint winner )
{
	#if HT8B_DEBUGGER
	_frp( FRP_LOW + " -> GAMEOVER" + FRP_END );
	#endif

	sn_gameover = true;
	sn_winnerid = winner;

	_netpack( sn_turnid );
	_netread();

	_onlocal_gameover();
}

void _turn_pass()
{
	#if HT8B_DEBUGGER
	_frp( FRP_LOW + " -> PASS" + FRP_END );
	#endif

	sn_permit = true;

	_netpack( sn_turnid ^ 0x1u );
	_netread();
}

void _turn_foul()
{
	#if HT8B_DEBUGGER
	_frp( FRP_LOW + " -> FOUL" + FRP_END );
	#endif

	sn_foul = true;
	sn_permit = true;

	_netpack( sn_turnid ^ 0x1U );
	_netread();
}

void _turn_continue()
{
	#if HT8B_DEBUGGER
	_frp( FRP_LOW + " -> COTNINUE" + FRP_END );
	#endif

	// Close table if it was open ( 8 ball specific )
	if( gm_is_0 )
	{
		if( sn_open )
		{
			uint sink_orange = 0;
			uint sink_blue = 0;
			uint pmask = sn_pocketed >> 2;

			for( int i = 0; i < 7; i ++ )
			{
				if( (pmask & 0x1u) == 0x1u )
					sink_blue ++;

				pmask >>= 1;
			}
			for( int i = 0; i < 7; i ++ )
			{
				if( (pmask & 0x1u) == 0x1u )
					sink_orange ++;

				pmask >>= 1;
			}

			if( sink_blue == sink_orange )
			{
				// Sunk equal amounts therefore still undecided
			}
			else
			{
				if( sink_blue > sink_orange )
				{
					sn_playerxor = sn_turnid;
				}
				else
				{
					sn_playerxor = sn_turnid ^ 0x1u;
				}

				sn_open = false;
				_onlocal_tableclosed();
			}
		}
	}

	// Keep playing
	sn_permit = true;

	_netpack( sn_turnid );
	_netread();
}

#endregion

#region R_MENU

						Vector3		m_planenormal;
						float			m_planedist;

						Vector3		m_cursor;
						bool			m_desktop			= true;

			const		float			k_mGmButtonW		= 0.09345f;
			const		float			k_mGmButtonH		= 0.034f;
			const		float			k_mSmolButtonR		= 0.034f;

			const 	float			k_mGmButtonA		= 0.01026977f;		// Reset height

[SerializeField]	GameObject[]	m_gamemode_buttons;
[SerializeField]	GameObject[]	m_join_buttons;
						
//[SerializeField]	GameObject		m_startbutton;
[SerializeField]	GameObject[]	m_teambuttons;
[SerializeField]	GameObject[]	m_timebuttons;

						bool[]			m_gm_buttonstates = new bool[4];
[SerializeField]	Mesh[]			m_buttonmeshes;

				const int				k_EButtonMesh_8ball		= 0;
				const int				k_EButtonMesh_9ball		= 1;
				const int				k_EButtonMesh_4ball		= 2;
				const int				k_EButtonMesh_reserved0 = 3;
				const int				k_EButtonMesh_green		= 4;
				const int				k_EButtonMesh_red			= 5;
				const int				k_EButtonMesh_blue		= 6;
				const int				k_EButtonMesh_triangle	= 7;
				const int				k_EButtonMesh_join_0		= 8;
				const int				k_EButtonMesh_join_1		= 9;
				const int				k_EButtonMesh_play		= 10;

				const uint				k_ButtonState_None			= 0x0u;
				const uint				k_ButtonState_Pressing		= 0x1u;
				const uint				k_ButtonState_Triggered		= 0x2u;
				const uint				k_ButtonState_ShouldReset	= 0x2u;
				const uint				k_ButtonState_FrameMask		= 0xFFFFFFFEu; // (~0x1u)

				// Current check dimensions ( pulled from above )
				float	m_current_x = 0.0f;
				float	m_current_y = 0.0f;
				Mesh	m_current_outline;
				MeshFilter	m_outline_filter;

				uint[]			m_auto_btnstate = new uint[20];
				GameObject[]	m_auto_btnobjs	= new GameObject[20];
				int				m_auto_id		= -1;

[SerializeField]	GameObject		m_gm_dkoutline;
[SerializeField]	GameObject[]	m_playerslot_owners;

				// VFX stuff
[SerializeField]	GameObject		m_TeamCover;
[SerializeField]	GameObject		m_TimeLimitDisp;

				Vector3					m_TeamCover_target_s;
				Vector3					m_TeamCover_current_s;

				Vector3					m_TimeLimit_x_target;
				Vector3					m_TimeLimit_x_current;

[SerializeField]	GameObject		m_menuLoc_main;
[SerializeField]	GameObject		m_menuLoc_start;
[SerializeField]	GameObject		m_newGameBtn;
[SerializeField]	Text[]			m_lobbyNames;
[SerializeField]	GameObject[]	rulePages;

				Vector3					m_menuLoc_sw;
				Vector3					m_menuLoc_swt;

VRCPlayerApi localplayer;

#if MENU_DEV

[SerializeField]	GameObject	m_devcursor;

#endif

Vector3 _plane_line_intersect( Vector3 n, float d, Vector3 a, Vector3 b )
{
    Vector3 ba = b-a;
    float nDotA = Vector3.Dot(n, a);
    float nDotBA = Vector3.Dot(n, ba);

    return a + (((d - nDotA)/nDotBA) * ba);
}

// Setup meshes on gameobject
void _htbtn_init( GameObject button, int variant, bool state )
{
	button.GetComponent< MeshFilter >().sharedMesh = m_buttonmeshes[ variant*3 + (state? 1: 0) ];
}

void _htmenu_init()
{
	// Setup button meshes
	_htbtn_init( m_gamemode_buttons[ 0 ], k_EButtonMesh_8ball, sn_gamemode == 0u );
	_htbtn_init( m_gamemode_buttons[ 1 ], k_EButtonMesh_9ball, sn_gamemode == 1u );
	_htbtn_init( m_gamemode_buttons[ 2 ], k_EButtonMesh_4ball, sn_gamemode == 2u );

	_htbtn_init( m_join_buttons[ 0 ], k_EButtonMesh_join_0, false );
	_htbtn_init( m_join_buttons[ 1 ], k_EButtonMesh_join_1, false );

	//_htbtn_init( m_startbutton, k_EButtonMesh_play, false );

	_htbtn_init( m_teambuttons[ 0 ], k_EButtonMesh_red,	!sn_teams );
	_htbtn_init( m_teambuttons[ 1 ], k_EButtonMesh_green,  sn_teams );

	_htbtn_init( m_timebuttons[ 0 ], k_EButtonMesh_triangle, true );
	_htbtn_init( m_timebuttons[ 1 ], k_EButtonMesh_triangle, true );
	_htbtn_init( m_newGameBtn, k_EButtonMesh_play, true );
	 
	m_outline_filter = m_gm_dkoutline.GetComponent<MeshFilter>();

	// Create surface plane
	m_planenormal = m_base.transform.up;
	m_planedist = Vector3.Dot( m_base.transform.position, m_planenormal );

	localplayer = Networking.LocalPlayer;

	m_gm_dkoutline.SetActive( false );

	sn_lobbyclosed = true;
	_htmenu_viewtimer();
	_htmenu_viewteams();
	_htmenu_viewgm();
	_htmenuview();
}

// View gamemode changes
void _htmenu_viewgm()
{
	for( int i = 0; i < m_gamemode_buttons.Length; i ++ )
	{
		if( sn_gamemode == (uint)i )
		{
			m_gamemode_buttons[ i ].GetComponent< MeshFilter >().sharedMesh = m_buttonmeshes[ (k_EButtonMesh_8ball + i) * 3 + 1 ];
		}
		else
		{
			m_gamemode_buttons[ i ].GetComponent< MeshFilter >().sharedMesh = m_buttonmeshes[ (k_EButtonMesh_8ball + i) * 3 ];
		}
	}
}

void _htmenu_viewjoin()
{
	int playernum = 0;

	if( !sn_lobbyclosed )
	{
		VRCPlayerApi host = Networking.GetOwner( m_playerslot_owners[ 0 ] );

		// Check out player names
		for( int i = 0; i < (sn_teams? 4: 2); i ++ )
		{
			VRCPlayerApi player = Networking.GetOwner( m_playerslot_owners[ i ] );

			// Mark host
			if( i == 0 )
			{
				m_lobbyNames[ i ].text = "<color=\"#f2ecb8\">"+player.displayName+"</color>";
				playernum ++;
			}
			else
			{
				// Its us
				if( local_playerid == i )
				{
					// Error: Local believes that we are in lobby, but someone else is there
					if( player.playerId != Networking.LocalPlayer.playerId )
					{
						#if HT8B_DEBUGGER
						_frp( FRP_ERR + "Error: de-sync local lobby status" + FRP_END );
						#endif

						local_playerid = -1;
						m_lobbyNames[ i ].text = "<color=\"#ff0000\">ERROR</color>";
					}
					else
					{
						playernum ++;
						m_lobbyNames[ i ].text = "<color=\"#cae4ed\">"+player.displayName+"</color>";
					}
				}
				else
				{
					// Player is joined
					if( host.playerId != player.playerId )
					{
						m_lobbyNames[ i ].text = "<color=\"#ffffff\">"+player.displayName+"</color>";
						playernum ++;
					}
					else
					{
						m_lobbyNames[ i ].text = "";
					}
				}
			}
		}
	}

	gm_practice = local_playerid == 0 && playernum == 1;

	// If in the game
	if( local_playerid >= 0 )
	{
		// Set our team button to the 'leave' button
		_htbtn_init( m_join_buttons[ local_teamid ], k_EButtonMesh_join_0 + (int)local_teamid, true );

		// Opposite button should become startgame/disabled, 'enabled' if player 0
		if( local_playerid == 0 )
		{
			m_join_buttons[ 1 ].SetActive( true );
			_htbtn_init( m_join_buttons[ 1 ], k_EButtonMesh_play, true );
		}
		else
		{
			m_join_buttons[ local_teamid ^ 0x1u ].SetActive( false );
		}
	}
	else // Otherwise, its just join buttons
	{
		m_join_buttons[ 0 ].SetActive( true );
		m_join_buttons[ 1 ].SetActive( true );
		_htbtn_init( m_join_buttons[ 0 ], k_EButtonMesh_join_0, false );
		_htbtn_init( m_join_buttons[ 1 ], k_EButtonMesh_join_1, false );
	}
}

uint last_viewtimer = 0u;
bool sound_spinning = false;

void _htmenu_viewtimer()
{
	if( last_viewtimer != sn_timer )
	{
		aud_main.PlayOneShot( snd_spin );
		last_viewtimer = sn_timer;
		sound_spinning = true;
	}

	m_TimeLimit_x_target = new Vector3( -0.128f * (float)sn_timer, 0.0f, 0.0f );
}

void _htmenu_viewteams()
{
	m_TeamCover_target_s = sn_teams? new Vector3(0,1,1): new Vector3(1,1,1);
	_htbtn_init( m_teambuttons[ 0 ], k_EButtonMesh_red,  !sn_teams );
	_htbtn_init( m_teambuttons[ 1 ], k_EButtonMesh_green, sn_teams );

	_htmenu_viewjoin();
}

void _htmenuview()
{
	if( sn_lobbyclosed )
	{
		m_menuLoc_swt = Vector3.one;
	}
	else
	{
		m_menuLoc_swt = Vector3.zero;
	}
}

uint	gm_target		= 0u;
float	gm_minheight	= Mathf.Infinity;

bool _buttonpressed( GameObject btn, int typeid )
{
	// Set automatic id's 
	m_auto_id ++;
	m_auto_btnobjs[ m_auto_id ] = btn;

	Vector3 delta; Vector3 tmp_pos;
	delta = btn.transform.localPosition - m_cursor;

	if( Mathf.Abs( delta.x ) < m_current_x && Mathf.Abs( delta.z ) <  m_current_y )
	{
		if( m_desktop )
		{
			// Visual transform
			if( Input.GetButton( "Fire1" ) )
			{
				tmp_pos = btn.transform.localPosition;
				tmp_pos.y = 0.0f;
				btn.transform.localPosition = tmp_pos;
			}

			m_gm_dkoutline.SetActive( true );
			m_gm_dkoutline.transform.localPosition = btn.transform.localPosition;
			m_gm_dkoutline.transform.localRotation = btn.transform.localRotation;

			m_outline_filter.sharedMesh = m_buttonmeshes[ typeid * 3 + 2 ];

			// Actuation
			if( Input.GetButtonDown( "Fire1" ) )
			{
				_htmenu_buttonpressed();
				return true;
			}
		}
		else // VR
		{
			// Button range
			if( m_cursor.y < k_mGmButtonA && m_cursor.y > -0.1f )
			{
				// Update visual position
				tmp_pos = btn.transform.localPosition;
				tmp_pos.y = Mathf.Clamp( m_cursor.y, 0.0f, tmp_pos.y );
				btn.transform.localPosition = tmp_pos;

				m_auto_btnstate[ m_auto_id ] |= k_ButtonState_Pressing;

				if( m_cursor.y <= 0.0f ) // Actuation
				{
					// Rising edge
					if( m_auto_btnstate[ m_auto_id ] == k_ButtonState_Pressing )
					{
						m_auto_btnstate[ m_auto_id ] |= k_ButtonState_Triggered;

						_htmenu_buttonpressed();
						return true;
					}
				}
			}
		}
	}

	return false;
}

// Join lobby
void _htjoinplayer( int id )
{
	#if HT8B_DEBUGGER
	_frp( FRP_YES + "_htjoinplayer: " + id + FRP_END );
	#endif

	local_playerid = id;
	local_teamid = ((uint)id & 0x2u) >> 1;
	Networking.SetOwner( Networking.LocalPlayer, m_playerslot_owners[ id ] );

	_htmenu_viewjoin();
}

// Join team locally
void _htjointeam( int id )
{
	#if HT8B_DEBUGGER
	_frp( FRP_LOW + "_htjointeam: " + id + FRP_END );
	#endif

	// Leave routine
	if( local_playerid >= 0 )
	{
		// Close lobby
		if( local_playerid == 0 )
		{
			if( id == 0 )
			{
				#if HT8B_DEBUGGER
				_frp( FRP_ERR + "( closing lobby )" + FRP_END );
				#endif

				sn_lobbyclosed = true;
				local_playerid = -1;

				_htmenuview();
				Networking.SetOwner( Networking.LocalPlayer, this.gameObject );
				_netpack_lossy();
			}
			else
			{
				#if HT8B_DEBUGGER
				_frp( FRP_YES + "Starting game!" + FRP_END );
				#endif

				region_selected = false;
				_tr_newgame();
				return;
			}
		}
		else
		{
			if( (int)local_teamid == id )
			{
				#if HT8B_DEBUGGER
				_frp( FRP_WARN + "( leaving lobby )" + FRP_END );
				#endif

				// Set owner back to host
				Networking.SetOwner( Networking.GetOwner( m_playerslot_owners[ 0 ] ), m_playerslot_owners[ local_playerid ] );

				// Mark locally out of game
				local_playerid = -1;
			}
			else
			{
				#if HT8B_DEBUGGER
				_frp( FRP_LOW + "this button does nothing" + FRP_END );
				#endif
			}
		}

		_htmenu_viewjoin();
		return;
	}

	// Create new lobby
	if( sn_lobbyclosed )
	{
		#if HT8B_DEBUGGER
		_frp( FRP_YES + "Creating lobby" + FRP_END );
		#endif

		// Assign other players to us to signify not joined
		Networking.SetOwner( Networking.LocalPlayer, m_playerslot_owners[ 1 ] );
		Networking.SetOwner( Networking.LocalPlayer, m_playerslot_owners[ 2 ] );
		Networking.SetOwner( Networking.LocalPlayer, m_playerslot_owners[ 3 ] );
		
		sn_lobbyclosed = false;
		
		Networking.SetOwner( Networking.LocalPlayer, this.gameObject );
		_netpack_lossy();

		_htjoinplayer( 0 );
		_htmenuview();
		return;
	}

	VRCPlayerApi gameHost = Networking.GetOwner( m_playerslot_owners[ 0 ] );

	// Check for open spot on team
	// Team 1
	if( id == 1 )
	{
		if( Networking.GetOwner( m_playerslot_owners[ 1 ] ).playerId == gameHost.playerId )
		{
			_htjoinplayer( 1 );
		}
		else if( sn_teams && (Networking.GetOwner( m_playerslot_owners[ 3 ] ).playerId == gameHost.playerId) )
		{
			_htjoinplayer( 3 );
		}
		else
		{
			#if HT8B_DEBUGGER
			_frp( FRP_ERR + "no slot availible" + FRP_END );
			#endif
		}
	} 

	// Team 2
	else if( sn_teams && (Networking.GetOwner( m_playerslot_owners[ 2 ] ).playerId == gameHost.playerId) )
	{
		_htjoinplayer( 2 );
	}
	else
	{
		#if HT8B_DEBUGGER
		_frp( FRP_ERR + "no slot availible" + FRP_END );
		#endif
	}
}

void _htmenu_resetnetwork()
{
	sn_lobbyclosed = true;

	if( Networking.GetOwner( this.gameObject ) == Networking.LocalPlayer )
	{
		Networking.SetOwner( Networking.LocalPlayer, m_playerslot_owners[ 0 ] );
		Networking.SetOwner( Networking.LocalPlayer, m_playerslot_owners[ 1 ] );
		Networking.SetOwner( Networking.LocalPlayer, m_playerslot_owners[ 2 ] );
		Networking.SetOwner( Networking.LocalPlayer, m_playerslot_owners[ 3 ] );

		_netpack_lossy();
	}
}

// Find button target
void _htmenu_trimin()
{
	m_auto_id = -1;

	GameObject btn;

	// Join / Leave buttons
	m_current_x = k_mGmButtonW;
	m_current_y = k_mGmButtonH;

	if( sn_lobbyclosed )
	{
		if( _buttonpressed( m_newGameBtn, k_EButtonMesh_play ) )
		{
			_htjointeam( 0 );
		}
	}
	else
	{
		for( int i = 0; i < m_join_buttons.Length; i ++ )
		{
			btn = m_join_buttons[ i ];

			if( _buttonpressed( btn, k_EButtonMesh_join_0 + i ) )
			{
				_htjointeam( i );
			}
		}

		if( local_playerid == 0 ) // Host only
		{
			// Gamemode buttons
			m_current_x = k_mGmButtonW;
			m_current_y = k_mGmButtonH;

			for( int i = 0; i < m_gamemode_buttons.Length; i ++ )
			{
				btn = m_gamemode_buttons[ i ];
		
				if( _buttonpressed( btn, k_EButtonMesh_8ball + i ) )
				{
					sn_gamemode = (uint)i;
					_htmenu_viewgm();
					_netpack_lossy();
				}
			}

			// Smol buttons
			m_current_x = k_mSmolButtonR;
			m_current_y = k_mSmolButtonR;

			// Timelimit buttons
			if( _buttonpressed( m_timebuttons[ 1 ], k_EButtonMesh_triangle ) )
			{
				if( sn_timer > 0 )
				{
					sn_timer --;

					_htmenu_viewtimer();
					_netpack_lossy();
				}
			}
			if( _buttonpressed( m_timebuttons[ 0 ], k_EButtonMesh_triangle ) )
			{
				if( sn_timer < 2 )
				{
					sn_timer ++;

					_htmenu_viewtimer();
					_netpack_lossy();
				}
			}

			// Teams enabled buttons
			if( _buttonpressed( m_teambuttons[ 0 ], k_EButtonMesh_red ) )
			{
				sn_teams = false;
				
				// Kick players
				Networking.SetOwner( Networking.LocalPlayer, m_playerslot_owners[ 2 ] );
				Networking.SetOwner( Networking.LocalPlayer, m_playerslot_owners[ 3 ] );

				_htmenu_viewteams();
				_netpack_lossy();
			}
			if( _buttonpressed( m_teambuttons[ 1 ], k_EButtonMesh_green ) )
			{
				sn_teams = true;

				_htmenu_viewteams();
				_netpack_lossy();
			}
		}
	}
}

VRC_Pickup.PickupHand _htmenu_hand = VRC_Pickup.PickupHand.None;

void _htmenu_buttonpressed()
{
	aud_main.PlayOneShot( snd_btn );

	if( _htmenu_hand != VRC_Pickup.PickupHand.None )
	{
		Networking.LocalPlayer.PlayHapticEventInHand( _htmenu_hand, 0.02f, 1.0f, 1.0f );
	}
}

void _htmenu_begin()
{
	Vector3 tmp_pos;
	GameObject btn;

	for( int i = 0; i <= m_auto_id; i ++ )
	{
		if( m_auto_btnstate[ i ] == k_ButtonState_ShouldReset )
		{
			m_auto_btnstate[ i ] = k_ButtonState_None;
		}

		// Reset button Y position
		btn = m_auto_btnobjs[ i ];
		tmp_pos = btn.transform.localPosition;
		tmp_pos.y = k_mGmButtonA;
		btn.transform.localPosition = tmp_pos;

		// Disables pressed so it can be re-set
		m_auto_btnstate[ i ] &= k_ButtonState_FrameMask;
	}
}

void _htmenu_enter()
{
	m_base.SetActive( true );
	_htmenu_resetnetwork();

	_htmenu_viewtimer();
	_htmenu_viewteams();
	_htmenu_viewgm();
	_htmenu_viewjoin();
	_htmenuview();
}

void _htmenu_exit()
{
	sn_lobbyclosed = true;
	m_base.SetActive( false );
}

float next_refresh = 0.0f;

void _htmenu_update()
{
	#if UNITY_EDITOR
	return;
	#endif

	m_desktop = !Networking.LocalPlayer.IsUserInVR();

	if( Time.timeSinceLevelLoad > next_refresh )
	{
		_htmenu_viewjoin();
		next_refresh = Time.timeSinceLevelLoad + 0.5f;
	}

	// Desktop: Project cursor onto plane
	if( m_desktop )
	{
		m_gm_dkoutline.SetActive( false );

		VRCPlayerApi.TrackingData hmd = localplayer.GetTrackingData( VRCPlayerApi.TrackingDataType.Head );
		m_cursor = _plane_line_intersect( m_planenormal, m_planedist, hmd.position, hmd.position + (hmd.rotation * Vector3.forward) );

		#if MENU_DEV
		m_devcursor.transform.position = m_cursor;
		#endif

		// Localize m_cursor
		m_cursor = m_base.transform.InverseTransformPoint( m_cursor );

		_htmenu_hand = VRC_Pickup.PickupHand.None;
		_htmenu_begin();
		_htmenu_trimin();
	}
	else
	{
		#if MENU_DEV
		m_devcursor.transform.position = localplayer.GetBonePosition( HumanBodyBones.RightIndexDistal );
		#endif

		_htmenu_begin(); 
		_htmenu_hand = VRC_Pickup.PickupHand.Left;

		// VR use figer tips / hand positions
		m_cursor = m_base.transform.InverseTransformPoint( localplayer.GetBonePosition( HumanBodyBones.LeftIndexDistal ) );
		_htmenu_trimin();

		m_cursor = m_base.transform.InverseTransformPoint( localplayer.GetBonePosition( HumanBodyBones.LeftIndexProximal ) );
		_htmenu_trimin();

		_htmenu_hand = VRC_Pickup.PickupHand.Right;
		m_cursor = m_base.transform.InverseTransformPoint( localplayer.GetBonePosition( HumanBodyBones.RightIndexDistal ) );
		_htmenu_trimin();

		m_cursor = m_base.transform.InverseTransformPoint( localplayer.GetBonePosition( HumanBodyBones.RightIndexProximal ) );
		_htmenu_trimin();
	}

	// Update visual stuff
	m_TeamCover_current_s = Vector3.Lerp( m_TeamCover_current_s, m_TeamCover_target_s, Time.deltaTime * 5.0f );
	m_TimeLimit_x_current = Vector3.Lerp( m_TimeLimit_x_current, m_TimeLimit_x_target, Time.deltaTime * 5.0f );
	m_menuLoc_sw = Vector3.Lerp( m_menuLoc_sw, m_menuLoc_swt, Time.deltaTime * 5.0f );

	// Stop sound
	if( sound_spinning && Vector3.Distance( m_TimeLimit_x_current, m_TimeLimit_x_target ) < 0.01f )
	{
		sound_spinning = false;
		aud_main.PlayOneShot( snd_spinstop );
	}

	m_TeamCover.transform.localScale = m_TeamCover_current_s;
	m_TimeLimitDisp.transform.localPosition = m_TimeLimit_x_current;

	// Menu locations scale swap
	m_menuLoc_start.transform.localScale = m_menuLoc_sw;
	m_menuLoc_main.transform.localScale = Vector3.one - m_menuLoc_sw;
}

#if UNITY_EDITOR
void HTEditorUI()
{
	
}
#endif

#endregion


float timeLast;
float accum;
// Copy current values to previous values, no memcpy here >:(
void _sn_cpyprev()
{
	// Init _prv states
	sn_turnid_prv = sn_turnid;
	sn_open_prv = sn_open;
	sn_gameover_prv = sn_gameover;
	sn_gameid_prv = sn_gameid;

	// Since 1.0.0
	sn_gamemode_prv = sn_gamemode;
	sn_timer_prv = sn_timer;
	sn_teams_prv = sn_teams;
	sn_lobbyclosed_prv = sn_lobbyclosed;

	//sn_pocketed_prv = sn_pocketed;		this one needs to be independent 
	//sn_simulating_prv = sn_simulating;
	//sn_foul_prv = sn_foul;
	//sn_playerxor_prv = sn_playerxor;
	//sn_winnerid_prv = sn_winnerid;
	//sn_permit_prv = sn_permit;
}

Vector3 dkCursor = new Vector3( 0.0f, 2.0f, 0.0f );
Vector3 dkHitCursor = new Vector3( 0.0f, 0.0f, 0.0f );

[SerializeField] GameObject dkCursorObj;
[SerializeField] GameObject dkHitPos;
[SerializeField] GameObject desktopBase;
[SerializeField] GameObject desktopQuad;
[SerializeField] GameObject[] dkStickBases;
[SerializeField] GameObject dkOverlayPwr;
[SerializeField] GameObject dk_E;

const float k_DesktopCursorSpeed = 0.035f;
bool dkShootingIn = false;
bool dkSafeRemove = true;
Vector3 dkShootVector;
Vector3 dkSafeRemovePoint;
float dkShootReference = 0.0f;
float dkClampX = k_TABLE_WIDTH;
float dkClampY = k_TABLE_HEIGHT;
bool turnLocalLive = false;

bool dkFrameIgnore = false;

// Cue picked up local
public void _ht_desktop_enter()
{
	dk_shootui = true;
	dkFrameIgnore = true;
	desktopBase.SetActive( true );

	// Lock player in place
	Networking.LocalPlayer.SetWalkSpeed( 0.0f );
	Networking.LocalPlayer.SetRunSpeed( 0.0f );
	Networking.LocalPlayer.SetStrafeSpeed( 0.0f );

	#if HT8B_DEBUGGER
	_frp( FRP_LOW + "Entering desktop overlay" + FRP_END );
	#endif
}

// Cue put down local
public void _ht_desktop_cue_down()
{
	_ht_desktopui_exit();
}

void _ht_desktopui_exit()
{
	dk_shootui = false;
	desktopBase.SetActive( false );

	#if !HT_QUEST

	gripControllers[ 0 ].dkPrimaryControl = true;
	gripControllers[ 1 ].dkPrimaryControl = true;

	Networking.LocalPlayer.SetWalkSpeed( 2.0f );
	Networking.LocalPlayer.SetRunSpeed( 4.0f );
	Networking.LocalPlayer.SetStrafeSpeed( 2.0f );

	#endif
}

void _dk_AllowHit()
{
	turnLocalLive = true;

	// Reset hit point
	dkHitCursor = Vector3.zero;
}

void _dk_DenyHit()
{
	turnLocalLive = false;
}

float shootAmt = 0.0f;

void _ht_desktopui_update()
{
	if( dkFrameIgnore )
	{
		dkFrameIgnore = false;
		return;
	}
	
	if( Input.GetKeyDown( KeyCode.E ) )
	{
		_ht_desktopui_exit();
		return;
	}

	// Keep UI rendering
	VRCPlayerApi.TrackingData hmd = localplayer.GetTrackingData( VRCPlayerApi.TrackingDataType.Head );
	desktopQuad.transform.position = hmd.position + hmd.rotation * Vector3.forward;
	dk_E.transform.position = desktopQuad.transform.position;

	dkCursor.x = Mathf.Clamp
	( 
		dkCursor.x + Input.GetAxis("Mouse X") * k_DesktopCursorSpeed,
		-dkClampX,
		 dkClampX
	);
	dkCursor.z = Mathf.Clamp
	( 
		dkCursor.z + Input.GetAxis("Mouse Y") * k_DesktopCursorSpeed,
		-dkClampY,
		 dkClampY
	);

	if( turnLocalLive )
	{
		Vector3 ncursor = dkCursor;
		ncursor.y = 0.0f;
		Vector3 delta = ncursor - ball_CO[ 0 ];
		GameObject cue = dkStickBases[ sn_turnid ];

		if( Input.GetButton( "Fire1" ) )
		{
			if( !dkShootingIn )
			{
				dkShootingIn = true;

				// Create shooting vector
				dkShootVector = delta.normalized;

				// Project reference start point
				dkShootReference = Vector3.Dot( dkShootVector, ncursor );

				// Create copy of cursor for later
				dkSafeRemovePoint = dkCursor;

				// Unlock cursor position from table
				dkClampX = Mathf.Infinity;
				dkClampY = Mathf.Infinity;
			}

			// Calculate shoot amount via projection
			shootAmt = dkShootReference - Vector3.Dot( dkShootVector, ncursor );
			dkSafeRemove = shootAmt < 0.0f;

			shootAmt = Mathf.Clamp( shootAmt, 0.0f, 0.5f );

			// Set delta back to dkShootVector
			delta = dkShootVector;

			// Disable cursor in shooting mode
			dkCursorObj.SetActive( false );
		}
		else
		{
			// Trigger shot
			if( dkShootingIn )
			{
				// Shot cancel
				if( dkSafeRemove )
				{

				}
				else // FIREEEEE 
				{
					// Fake hit ( kinda )
					float vel = Mathf.Pow( shootAmt * 2.0f, 1.4f )*9.0f;
				
					ball_V[ 0 ] = dkShootVector * vel;

					Vector3 r_1 = (RaySphere_output - ball_CO[ 0 ]) * k_BALL_1OR;
					Vector3 p = dkShootVector.normalized * vel;
					ball_W[ 0 ] = Vector3.Cross( r_1, p ) * -25.0f;

					#if HT8B_DEBUGGER
					_frp( FRP_WARN + "Angular velocity: " + ball_W[ 0 ].ToString() + ". Velocity: " + ball_V[ 0 ].ToString() + FRP_END );
					#endif

					cue.transform.localPosition = new Vector3( 2000.0f, 2000.0f, 2000.0f );
					turnLocalLive = false;
					_hit_general();
				}

				// Restore cursor position
				dkCursor = dkSafeRemovePoint;
				dkClampX = k_TABLE_WIDTH;
				dkClampY = k_TABLE_HEIGHT;

				// 1-frame override to fix rotation
				delta = dkShootVector;
			}

			dkShootingIn = false;
			shootAmt = 0.0f;
			dkCursorObj.SetActive( true );
		}

		if( Input.GetKey( KeyCode.W ) )
		{
			dkHitCursor += Vector3.forward * Time.deltaTime;
		}
		if( Input.GetKey( KeyCode.S ) )
		{
			dkHitCursor += Vector3.back * Time.deltaTime;
		}
		if( Input.GetKey( KeyCode.A ) )
		{
			dkHitCursor += Vector3.left * Time.deltaTime;
		}
		if( Input.GetKey( KeyCode.D ) )
		{
			dkHitCursor += Vector3.right * Time.deltaTime;
		}

		// Clamp in circle
		if( dkHitCursor.magnitude > 0.90f )
		{
			dkHitCursor = dkHitCursor.normalized * 0.9f;
		}

		dkHitPos.transform.localPosition = dkHitCursor;
	
		// Get angle
		float ang = Mathf.Atan2( delta.x, delta.z );

		// Create rotation
		Quaternion xr = Quaternion.AngleAxis( 10.0f, Vector3.right );
		Quaternion r = Quaternion.AngleAxis( ang * Mathf.Rad2Deg, Vector3.up );
		
		Vector3 worldHit = new Vector3(  dkHitCursor.x * k_BALL_PL_X, dkHitCursor.z * k_BALL_PL_X, -0.89f -shootAmt );

		cue.transform.localRotation = r * xr;
		cue.transform.position = this.transform.TransformPoint( ball_CO[ 0 ] + (r * xr * worldHit) );
	}

	dkCursorObj.transform.localPosition = dkCursor;
	dkOverlayPwr.transform.localScale = new Vector3( 1.0f-(shootAmt*2.0f), 1.0f, 1.0f );
}

#region R_UNITY_MAGIC

void _hit_general()
{
	// Make sure repositioner is turned off if the player decides he just
	// wanted to hit it without putting it somewhere
	isReposition = false;
	markerObj.SetActive( false );
	devhit.SetActive( false );
	guideline.SetActive( false );

	// Remove locks
	_tr_endhit();
	sn_permit = false;
	sn_foul = false;	// In case did not drop foul marker

	#if HT8B_DEBUGGER
	_frp( FRP_LOW + "Commiting changes" + FRP_END );
	#endif

	// Commit changes
	sn_simulating = true;
	sn_pocketed_prv = sn_pocketed;

	// Make sure we definately are the network owner
	Networking.SetOwner( Networking.LocalPlayer, this.gameObject );

	_netpack( sn_turnid );
	_netread();

	sn_oursim = true;

	AudioSource.PlayClipAtPoint( snd_hitball, cuetip.transform.position, 1.0f );
}

private void Update()
{
	// Physics step accumulator routine
	float time = Time.timeSinceLevelLoad;
	float timeDelta = time - timeLast;

	timeLast = time;

	if( particle_alive )
	{
		_vis_floaty_eval();
	}

	if( dk_shootui )
	{
		_ht_desktopui_update();
	}
		
	// Run sim only if things are moving
	if( sn_simulating )
	{
		accum += timeDelta;

		if ( accum > k_MAX_DELTA )
		{
			accum = k_MAX_DELTA;
		}

		while ( accum >= k_FIXED_TIME_STEP )
		{
			_phys_step();
			accum -= k_FIXED_TIME_STEP;
		}
	}
	else
	{
		// Control is in menu behaviour
		if( sn_gameover )
		{
			_htmenu_update();
			return;
		}
	}

	// Update rendering objects positions
	uint ball_bit = 0x1u;
	for( int i = 0; i < 16; i ++ )
	{
		if( (ball_bit & sn_pocketed) == 0x0u )
		{
			balls_render[i].transform.localPosition = ball_CO[ i ];
		}

		ball_bit <<= 1;
	}

	cue_lpos = this.transform.InverseTransformPoint( cuetip.transform.position );
	Vector3 lpos2 = cue_lpos;

	// if shot is prepared for next hit
	if( sn_permit )
	{
		bool isContact = false;

		if( isReposition )
		{
			// Clamp position to table / kitchen
			Vector3 temp = markerObj.transform.localPosition;
			temp.x = Mathf.Clamp( temp.x, -k_TABLE_WIDTH, repoMaxX );
			temp.z = Mathf.Clamp( temp.z, -k_TABLE_HEIGHT, k_TABLE_HEIGHT );
			temp.y = 0.0f;
			markerObj.transform.localPosition = temp;
			markerObj.transform.localRotation = Quaternion.identity;

			ball_CO[ 0 ] = temp;
			balls_render[ 0 ].transform.localPosition = temp;

			isContact = _cue_contacting();

			if( isContact )
			{
				markerMaterial.SetColor( uniform_marker_colour, k_markerColourNO );
			}
			else
			{
				markerMaterial.SetColor( uniform_marker_colour, k_markerColourOK );
			}
		}

		Vector3 cueball_pos = ball_CO[ 0 ];

		if( sn_armed && !isContact )
		{
			float sweep_time_ball = Vector3.Dot( cueball_pos - cue_llpos, cue_vdir );

			// Check for potential skips due to low frame rate
			if( sweep_time_ball > 0.0f && sweep_time_ball < (cue_llpos - lpos2).magnitude )
			{
				lpos2 = cue_llpos + cue_vdir * sweep_time_ball;
			}

			// Hit condition is when cuetip is gone inside ball
			if( (lpos2 - cueball_pos).sqrMagnitude < k_BALL_RSQR )
			{
				Vector3 horizontal_force = lpos2 - cue_llpos;
				horizontal_force.y = 0.0f;

				// Compute velocity delta
				float vel = (horizontal_force.magnitude / Time.deltaTime) * 1.5f;

				// Clamp velocity input to 20 m/s ( moderate break speed )
				ball_V[ 0 ] = cue_shotdir * Mathf.Min( vel, 20.0f );

				// Angular velocity: L=r(normalized)×p
				Vector3 r = (RaySphere_output - cueball_pos) * k_BALL_1OR;
				Vector3 p = cue_vdir * vel;
				ball_W[ 0 ] = Vector3.Cross( r, p ) * -50.0f;

				#if HT8B_DEBUGGER
				_frp( FRP_WARN + "Angular velocity: " + ball_W[ 0 ].ToString() + ". Velocity: " + ball_V[ 0 ].ToString() + FRP_END );
				#endif

				_hit_general();
			}
		}
		else
		{
			cue_vdir = this.transform.InverseTransformVector( cuetip.transform.forward );//new Vector2( cuetip.transform.forward.z, -cuetip.transform.forward.x ).normalized;

			// Get where the cue will strike the ball
			if( _phy_ray_sphere( lpos2, cue_vdir, cueball_pos ))
			{
				guideline.SetActive( true );
				devhit.SetActive( true );
				devhit.transform.localPosition = RaySphere_output;

				cue_shotdir = cue_vdir;
				cue_shotdir.y = 0.0f;

				if( dk_shootui )
				{
				}
				else
				{
					// Compute deflection in VR mode
					Vector3 scuffdir = ( cueball_pos - RaySphere_output );
					scuffdir.y = 0.0f;
					cue_shotdir += scuffdir.normalized * 0.17f;
				}

				cue_fdir = Mathf.Atan2( cue_shotdir.z, cue_shotdir.x );

				// Update the prediction line direction
				guideline.transform.localPosition = ball_CO[ 0 ];
				guideline.transform.localEulerAngles = new Vector3( 0.0f, -cue_fdir * Mathf.Rad2Deg, 0.0f );
			}
			else
			{
				devhit.SetActive( false );
				guideline.SetActive( false );
			}
		}
	}

	cue_llpos = lpos2;

	// Table outline colour
	if( sn_gameover )
	{
		// Flashing if we won
		#if !HT_QUEST
		tableCurrentColour = tableSrcColour * (Mathf.Sin( Time.timeSinceLevelLoad * 3.0f) * 0.5f + 1.0f);
		#endif
		
		infBaseTransform.transform.localPosition = new Vector3( 0.0f, Mathf.Sin( Time.timeSinceLevelLoad ) * 0.1f, 0.0f );
		infBaseTransform.transform.Rotate( Vector3.up, 90.0f * Time.deltaTime );
	}
	else
	{
		#if !HT_QUEST
		tableCurrentColour = Color.Lerp( tableCurrentColour, tableSrcColour, Time.deltaTime * 3.0f );
		#else

		// Run uniform updates at a slower rate on android (/8)
		ANDROID_UNIFORM_CLOCK ++;

		if( ANDROID_UNIFORM_CLOCK >= ANDROID_CLOCK_DIVIDER )
		{
			tableCurrentColour = Color.Lerp( tableCurrentColour, tableSrcColour, Time.deltaTime * 24.0f );
			tableMaterial.SetColor( uniform_tablecolour, tableCurrentColour );

			ANDROID_UNIFORM_CLOCK = 0x00u;
		}

		#endif
	}

	float time_percentage;
	if( timer_running )
	{
		float timeleft = timer_end - Time.timeSinceLevelLoad;

		if( timeleft < 0.0f )
		{
			_onlocal_timer_end();
			time_percentage = 0.0f;
		}
		else
		{
			time_percentage = 1.0f - (timeleft * timer_recip);
		}
	}
	else
	{
		time_percentage = 0.0f;
	}

#if !HT_QUEST
	tableMaterial.SetColor( uniform_tablecolour, 
		new Color( tableCurrentColour.r, tableCurrentColour.g, tableCurrentColour.b, time_percentage ) );
#endif

	// Intro animation
	if( introAminTimer > 0.0f )
	{
		introAminTimer -= Time.deltaTime;

		Vector3 temp;
		float atime;
		float aitime;

		if( introAminTimer < 0.0f )
			introAminTimer = 0.0f;

		// Cueball drops late
		temp = balls_render[0].transform.localPosition;
		atime = Mathf.Clamp(introAminTimer - 0.33f, 0.0f, 1.0f); 
		aitime = (1.0f - atime);
		temp.y = Mathf.Abs(Mathf.Cos(atime * 6.29f)) * atime * 0.5f;
		balls_render[0].transform.localPosition = temp;
		balls_render[0].transform.localScale = new Vector3(aitime, aitime, aitime);

		for ( int i = 1; i < 16; i ++ )
		{
			temp = balls_render[i].transform.localPosition;
			atime = Mathf.Clamp(introAminTimer - 0.84f - (float)i * 0.03f, 0.0f, 1.0f);
			aitime = (1.0f - atime);

			temp.y = Mathf.Abs( Mathf.Cos( atime * 6.29f ) ) * atime * 0.5f;
			balls_render[i].transform.localPosition = temp;
			balls_render[i].transform.localScale = new Vector3(aitime, aitime, aitime);
		}
	}
}

private void Start()
{
	aud_main = this.GetComponent<AudioSource>();
	_htmenu_init();
	_sn_cpyprev();

	#if HT8B_DEBUGGER
	_frp( FRP_LOW + "Starting" + FRP_END );
	#endif

	#if COMPILE_FUNC_TESTS
	_setup_break();
	#endif
	
	guidelineMat.SetMatrix( "_BaseTransform", this.transform.worldToLocalMatrix );

	if (Table_Probe != null)
	{
		Table_Probe.gameObject.SetActive(true);
		Table_Probe.mode = ReflectionProbeMode.Realtime;
		Table_Probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
		Table_Probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
		Table_Probe.RenderProbe();
	}

	// turn off guideline
	_vis_disableobjects();
}

#endregion

int[] break_order_8ball = { 9, 2, 10, 11, 1, 3, 4, 12, 5, 13, 14, 6, 15, 7, 8 };
int[] break_order_9ball = { 2, 3, 4, 5, 9, 6, 7, 8, 1 };
int[] break_rows_9ball = { 0, 1, 2, 1, 0 }; 

// Resets local game state to defined state
// TODO: Merge this with NewGame()
public void _setup_break()
{
	#if HT8B_DEBUGGER
	_frp( FRP_LOW + "SetupBreak()" + FRP_END );
	#endif

	sn_simulating = false;
	sn_open = true;
	sn_gameover = false;
	sn_playerxor = 0;
	sn_winnerid = 0;

	// Cue ball
	ball_CO[ 0 ] = new Vector3( -k_SPOT_POSITION_X, 0.0f, 0.0f );
	ball_V[ 0 ] = Vector3.zero;

	// Start at spot

	if( gm_is_1 ) // 9 ball
	{
		sn_pocketed = 0xFC00u;

		for( int i = 0, k = 0; i < 5; i ++ )
		{
			int rown = break_rows_9ball[ i ];
			for( int j = 0; j <= rown; j ++ )
			{
				ball_CO[ break_order_9ball[ k ++ ] ] = new Vector3
				( 
					k_SPOT_POSITION_X + (float)i * k_BALL_PL_Y + UnityEngine.Random.Range(-k_RANDOMIZE_F, k_RANDOMIZE_F), 
					0.0f, 
					(float)(-rown + j * 2) * k_BALL_PL_X + UnityEngine.Random.Range(-k_RANDOMIZE_F, k_RANDOMIZE_F)
				);

				ball_V[ k ] = Vector3.zero;
				ball_W[ k ] = Vector3.zero;
			}
		}
	}
	else if( gm_is_2 ) // 4 ball
	{
		sn_pocketed = 0xFDF2u;

		ball_CO[ 0 ] = new Vector3( -k_SPOT_CAROM_X, 0.0f, 0.0f );
		ball_CO[ 9 ] = new Vector3(  k_SPOT_CAROM_X, 0.0f, 0.0f );
		ball_CO[ 2 ] = new Vector3(  k_SPOT_POSITION_X, 0.0f, 0.0f );
		ball_CO[ 3 ] = new Vector3( -k_SPOT_POSITION_X, 0.0f, 0.0f );

		ball_V[ 0 ] = Vector3.zero;
		ball_V[ 9 ] = Vector3.zero;
		ball_V[ 2 ] = Vector3.zero;
		ball_V[ 3 ] = Vector3.zero;

		ball_W[ 0 ] = Vector3.zero;
		ball_W[ 9 ] = Vector3.zero;
		ball_W[ 2 ] = Vector3.zero;
		ball_W[ 3 ] = Vector3.zero;
	}
	else // Normal 8 ball modes
	{
		sn_pocketed = 0x00u;

		for( int i = 0, k = 0; i < 5; i ++ )
		{
			for( int j = 0; j <= i; j ++ )
			{
				ball_CO[ break_order_8ball[ k ++ ] ] = new Vector3
				( 
					k_SPOT_POSITION_X + (float)i * k_BALL_PL_Y + UnityEngine.Random.Range(-k_RANDOMIZE_F, k_RANDOMIZE_F), 
					0.0f, 
					(float)(-i + j * 2) * k_BALL_PL_X + UnityEngine.Random.Range(-k_RANDOMIZE_F, k_RANDOMIZE_F)
				);

				ball_V[ k ] = Vector3.zero;
				ball_W[ k ] = Vector3.zero;
			}
		}
	}

	sn_pocketed_prv = sn_pocketed;
}


#region R_INTERFACING

// Purpose: 
//  Public methods which should are called from other behaviours

// Player select 4 ball mode Japanese
public void _tr_yotsudama()
{
	fb_jp = true;
	fb_kr = false;
	region_selected = true;
	select4b.SetActive( false );

	_tr_newgame();
}

public void _tr_sagu()
{
	fb_jp = false;
	fb_kr = true;
	region_selected = true;
	select4b.SetActive( false );

	_tr_newgame();
}

// Player is holding input trigger
public void _tr_starthit()
{
	// lock aim variables
	bool isOurTurn = ((local_playerid >= 0) && (local_teamid == sn_turnid)) || gm_practice;

	if( isOurTurn )
	{
		sn_armed = true;

		#if !HT_QUEST
		guidelineMat.SetColor( "_Colour", k_aimColour_locked );
		#endif
	}
}

// Player stopped holding input trigger
public void _tr_endhit()
{
	sn_armed = false;

	#if !HT_QUEST
	guidelineMat.SetColor( "_Colour", k_aimColour_aim );
	#endif 
}

// Player was moving cueball, place it down
public void _tr_placeball()
{
	if( !_cue_contacting() )
	{
		isReposition = false;
		markerObj.SetActive( false );

		sn_permit = true;
		sn_foul = false;

		Networking.SetOwner( Networking.LocalPlayer, this.gameObject );

		// Save out position to remote clients
		_netpack( sn_turnid );
		_netread();
	}
}

// Initialize new match as the host
public void _tr_newgame()
{
	// Check if game in progress
	if( sn_gameover )
	{
		#if HT8B_DEBUGGER
		_frp( FRP_YES + "Starting new game" + FRP_END );
		#endif

		// Get gamestate rolling
		sn_gameid ++;
		sn_permit = true;

		_onlocal_newgame();

		sn_turnid = 0;
		sn_turnid_prv = 0;
		_onlocal_turnchange();

		// Following is overrides of NewGameLocal, for game STARTER only
		_setup_break();
		_vis_apply_tablecolour(0);

		Networking.SetOwner( Networking.LocalPlayer, this.gameObject );
		_netpack( 0 );
		_netread();

		// Override allow repositioning within kitchen
		// Local effector
		isReposition = true;
		repoMaxX = -k_SPOT_POSITION_X;
		markerObj.transform.localPosition = ball_CO[ 0 ];
		markerObj.SetActive( true );

		if( !region_selected )
		{
			if( sn_gamemode == 2u )
			{
				select4b.SetActive( true );
				return;
			}
		}
	}
	else
	{
		// Should not be hit since v1.0.0
		#if HT8B_DEBUGGER
		_frp( FRP_ERR + "game in progress" + FRP_END );
		#endif
	}
}

// Completely reset ht8b state
public void _tr_force_end()
{
	// Limit reset to totem owners ownly, this will always be someone in the room
	// but it may not be obvious to players who has the ownership. So a info text
	// is added above the reset button telling them who can reset if they dont have it
	// this is simply to prevent trolls running in and force resetting at random

	if( Networking.LocalPlayer == Networking.GetOwner( playerTotems[0] ) ||
		Networking.LocalPlayer == Networking.GetOwner( playerTotems[1] )
		|| sn_gameover )
	{
		#if HT8B_DEBUGGER
		_frp( FRP_WARN + "Ending game early" + FRP_END );
		#endif

		sn_gameover = true;
		sn_permit = false;
		sn_simulating = false;

		// For good measure in case other clients trigger an event whilst owner
		sn_packetid += 2;

		Networking.SetOwner( Networking.LocalPlayer, this.gameObject );
		_netpack( sn_turnid );
		_netread();

		_onlocal_gameover();

		infReset.text = "Reset" ;
	}
	else
	{
		// TODO: Make this a panel
		#if HT8B_DEBUGGER
		_frp( FRP_ERR + "Reset is availible to: " + Networking.GetOwner( playerTotems[0] ).displayName + " and " + Networking.GetOwner( playerTotems[1] ).displayName + FRP_END );
		#endif

		infReset.text = "Only:\n" + Networking.GetOwner( playerTotems[0] ).displayName + " and " + Networking.GetOwner( playerTotems[1] ).displayName + "\ncan reset";
	}
}

#endregion

#region R_NETWORK

const float I16_MAXf = 32767.0f;

void _encode_u16( int pos, ushort v ) 
{
	net_data[ pos ] = (byte)(v & 0xff);
	net_data[ pos + 1 ] = (byte)(((uint)v >> 8) & 0xff);
}

ushort _decode_u16( int pos ) 
{
	return (ushort)(net_data[pos] | (((uint)net_data[pos+1]) << 8));
}

// 4 char string from Vector2. Encodes floats in: [ -range, range ] to 0-65535
void _encode_vec3( int pos, Vector3 vec, float range )
{
	_encode_u16( pos, (ushort)((vec.x / range) * I16_MAXf + I16_MAXf ) );
	_encode_u16( pos + 2, (ushort)((vec.z / range) * I16_MAXf + I16_MAXf ) );
}

// 6 char string from Vector3. Encodes floats in: [ -range, range ] to 0-65535
void _encode_vec3_full( int pos, Vector3 vec, float range )
{
	_encode_u16( pos, (ushort)((Mathf.Clamp( vec.x, -range, range ) / range) * I16_MAXf + I16_MAXf ) );
	_encode_u16( pos + 2, (ushort)((Mathf.Clamp( vec.y, -range, range ) / range) * I16_MAXf + I16_MAXf ) );
	_encode_u16( pos + 4, (ushort)((Mathf.Clamp( vec.z, -range, range ) / range) * I16_MAXf + I16_MAXf ) );
}

// Decode 4 chars at index to Vector3 (x,z). Decodes from 0-65535 to [ -range, range ]
Vector3 _decode_vec3( int start, float range )
{
	ushort _x = _decode_u16( start );
	ushort _y = _decode_u16( start + 2 );

	float x = ((_x - I16_MAXf) / I16_MAXf) * range;
	float y = ((_y - I16_MAXf) / I16_MAXf) * range;
		
	return new Vector3( x, 0.0f, y );
}

// Decode 6 chars at index to Vector3. Decodes from 0-65535 to [ -range, range ]
Vector3 _decode_vec3_full( int start, float range )
{
	ushort _x = _decode_u16( start );
	ushort _y = _decode_u16( start + 2 );
	ushort _z = _decode_u16( start + 4 );

	float x = ((_x - I16_MAXf) / I16_MAXf) * range;
	float y = ((_y - I16_MAXf) / I16_MAXf) * range;
	float z = ((_z - I16_MAXf) / I16_MAXf) * range;
		
	return new Vector3( x, y, z );
} 

public void _netpack_lossy()
{
	if( !sn_gameover )
	{
		#if HT8B_DEBUGGER
		_frp( FRP_ERR + "Critical error: gameover was false when trying to _netpack_lossy()" + FRP_END );
		#endif
		return;
	}

	// Game state
	uint flags = 0x20u;						// bit #

	// Since v1.0.0
	flags |= sn_gamemode << 8;				// 8  - 3 bits
	flags |= sn_timer << 13;				// 13 - 2 bits
	if( sn_teams ) flags |= 0x8000u;		// 15 - 1 bit
	if( sn_lobbyclosed ) flags |= 0x800u;

	_encode_u16( 0x4C, (ushort)flags );

	sn_packetid = (ushort)(sn_packetid + 1u);
	_encode_u16( 0x4E, (ushort)(sn_packetid) );
	_encode_u16( 0x50, sn_gameid );

	netstr = Convert.ToBase64String( net_data, Base64FormattingOptions.None );
}

// Encode all data of game state into netstr
public void _netpack( uint _turnid )
{
	if( local_playerid < 0 )
	{
		#if HT8B_DEBUGGER
		_frp( FRP_ERR + "Critical error: local_playerid was less than 0 when trying to NetPack()" + FRP_END );
		#endif
		return;
	}

	// Garuntee array size by reallocating.. because c#
	net_data = new byte[0x52];

	for ( int i = 0; i < 16; i ++ )
	{
		_encode_vec3( i * 4, ball_CO[ i ], 2.5f );
	}

	// Cue ball velocity & angular velocity last
	_encode_vec3( 0x40, ball_V[ 0 ], 50.0f );
	_encode_vec3_full( 0x44, ball_W[ 0 ], 500.0f );

	if( gm_is_2 )
	{
		// Encode player scores into gmspec
		sn_gmspec  = (ushort)(((uint)fb_scores[ 0 ]) & 0x0fu);
		sn_gmspec |= (ushort)((((uint)fb_scores[ 1 ]) & 0x0fu) << 4);
		if( fb_kr ) sn_gmspec |= (ushort)0x100u;

		// 4 ball specifc ( no pocket info )
		_encode_u16( 0x4A, sn_gmspec );
	}
	else
	{
		// Encode pocketed imformation
		_encode_u16( 0x4A, (ushort)(sn_pocketed & 0x0000FFFFu) );
	}

	// Game state
	uint flags = 0x0U;						// bit #
	if( sn_simulating ) flags |= 0x1U;	// 0
	flags |= _turnid << 1;					// 1
	if( sn_foul ) flags |= 0x4U;			// 2
	if( sn_open ) flags |= 0x8U;			// 3
	flags |= sn_playerxor << 4;			// 4
	if( sn_gameover ) flags |= 0x20u;	// 5
	flags |= sn_winnerid << 6;				// 6
	if( sn_permit ) flags |= 0x80U;		// 7

	if( sn_lobbyclosed ) flags |= 0x800u;

	// Since v1.0.0
	flags |= sn_gamemode << 8;				// 8  - 3 bits
	flags |= sn_timer << 13;				// 13 - 2 bits
	if( sn_teams ) flags |= 0x8000u;		// 15 - 1 bit

	_encode_u16( 0x4C, (ushort)flags );

	// Player ID msb gets added to referee any discrepencies between clients
	// Higher order players get priority because it will be less common
	// to play 2v2, so we can save most packet id's for normal 1v1
	uint msb_playerid = ((uint)local_playerid & 0x2u) >> 1;

	_encode_u16( 0x4E, (ushort)(sn_packetid + 1u + msb_playerid) );
	_encode_u16( 0x50, sn_gameid );

	netstr = Convert.ToBase64String( net_data, Base64FormattingOptions.None );

	#if HT8B_DEBUGGER
	_frp( FRP_LOW + "NetPack()" + FRP_END );
	#endif
}

// Decode networking string
// TODO: Clean up this function
public void _netread()
{
	// CHECK ERROR ===================================================================================================
	#if HT8B_DEBUGGER
	_frp( FRP_LOW + "incoming base64: " + netstr + FRP_END );
	#endif

	byte[] in_data = Convert.FromBase64String( netstr );
	if( in_data.Length < 0x52 ) {
			
		#if HT8B_DEBUGGER
		_frp( FRP_WARN + "Sync string too short for decode, skipping\n" + FRP_END );
		#endif

		return; 
	}

	net_data = in_data;

	#if HT8B_DEBUGGER
	_frp( FRP_LOW + _netstr_hex() + FRP_END );
	#endif

	// Throw out updates that are possible errournous
	ushort nextid = _decode_u16( 0x4E );
	if( nextid <= sn_packetid )
	{
		#if HT8B_DEBUGGER
		_frp( FRP_WARN + "Packet ID was old ( " + nextid + " <= " + sn_packetid + " ). Throwing out update" + FRP_END );
		#endif

		return;
	}
	sn_packetid = nextid;

	// MAIN DECODE ===================================================================================================
	_sn_cpyprev();

	// Pocketed information
	// Ball positions, reset velocity
	
	for( int i = 0; i < 16; i ++ )
	{
		ball_V[ i ] = Vector3.zero;
		ball_W[ i ] = Vector3.zero;
		ball_CO[ i ] = _decode_vec3( i * 4, 2.5f );
	}

	ball_V[ 0 ] = _decode_vec3( 0x40, 50.0f );
	ball_W[ 0 ] = _decode_vec3_full( 0x44, 500.0f );

	sn_pocketed = _decode_u16( 0x4A );

	uint gamestate = _decode_u16( 0x4C );
	sn_simulating = (gamestate & 0x1U) == 0x1U;
	sn_turnid = (gamestate & 0x2U) >> 1;
	sn_foul = (gamestate & 0x4U) == 0x4U;
	sn_open = (gamestate & 0x8U) == 0x8U;
	sn_playerxor = (gamestate & 0x10U) >> 4;
	sn_gameover = (gamestate & 0x20U) == 0x20U;
	sn_winnerid = (gamestate & 0x40U) >> 6;
	sn_permit = (gamestate & 0x80U) == 0x80U;	
	sn_lobbyclosed = (gamestate & 0x800u) == 0x800u;

	// Since v1.0.0
	sn_gamemode = (gamestate & 0x700u) >> 8;			// 3 bits
	sn_timer = (gamestate & 0x6000u) >>  13;			// 2 bits
	sn_teams = (gamestate & 0x8000u) == 0x8000u;		//

	// TODO: allocate more bits to packet ID, less to game ID
	sn_gameid = _decode_u16( 0x50 );

	// Events ==========================================================================================================

	if( sn_gameid > sn_gameid_prv && !sn_gameover )
	{
		// EV: 1
		#if HT8B_DEBUGGER
		_frp( FRP_YES + " .EV: 1 (sn_gameid > sn_gameid_prv) -> NewGame" + FRP_END );
		#endif

		_onlocal_newgame();
	}

	// Check if turn was transferred
	if( sn_turnid != sn_turnid_prv )
	{
		// EV: 2
		#if HT8B_DEBUGGER
		_frp( FRP_YES + " .EV: 2 (sn_turnid != sn_turnid_prv) -> NewTurn" + FRP_END );
		#endif

		_onlocal_turnchange();
	}

	// Table switches to closed
	if( sn_open_prv && !sn_open )
	{
		// EV: 3
		#if HT8B_DEBUGGER
		_frp( FRP_YES + " .EV: 3 (sn_open_prv && !sn_open) -> DisplaySet" + FRP_END );
		#endif

		_onlocal_tableclosed();
	}

	// Check if game is over
	if(!sn_gameover_prv && sn_gameover)
	{
		// EV: 4
		#if HT8B_DEBUGGER
		_frp( FRP_YES + " .EV: 4 (!sn_gameover_prv && sn_gamemover) -> Gameover" + FRP_END );
		#endif

		_onlocal_gameover();
		return;
	}

	if( sn_gameover )
	{
		_htmenu_viewtimer();
		_htmenu_viewteams();
		_htmenu_viewgm();
		_htmenu_viewjoin();
		_htmenuview();
		return;
	}

	// Effects colliders need to be turned off when not simulating
	// to improve pickups being glitchy
	if( sn_simulating )
	{
		fxColliderBase.SetActive( true );
	}
	else
	{
		fxColliderBase.SetActive( false );
	}

	if( gm_is_2 )
	{
		sn_gmspec = _decode_u16( 0x4A );
		fb_scores[ 0 ] = (int)(sn_gmspec & 0x0fu);
		fb_scores[ 1 ] = (int)((sn_gmspec & 0xf0u) >> 4);

		fb_kr = (sn_gmspec & 0x100u) == 0x100u;
		fb_jp = !fb_kr;

		sn_pocketed = 0xFDF2u;
	}

	// Check this every read
	// Its basically 'turn start' event
	if( sn_permit )
	{
		bool isOurTurn = ((local_playerid >= 0) && (local_teamid == sn_turnid)) || gm_practice;
		
		// Check if teammate placed the positioner
		if( !sn_foul )
		{
			#if HT8B_DEBUGGER
			_frp( FRP_YES + " .EV: 3 (!sn_foul && sn_foul_prv && sn_permit) -> Marker placed" + FRP_END );
			#endif

			isReposition = false;
			markerObj.SetActive( false );
		}

		#if !HT_QUEST
		if( isOurTurn )
		{
			// Update for desktop
			_dk_AllowHit();	
		}
		else
		{
			_dk_DenyHit();
		}
		#endif

		if( gm_is_1 )
		{
			int target = _lowest_ball( sn_pocketed );

			marker9ball.SetActive( true );
			marker9ball.transform.localPosition = ball_CO[ target ];
		}

		#if !HT_QUEST
		_vis_rackballs();
		#endif

		if( sn_timer > 0 && !timer_running )
		{
			_timer_reset();
		}
	}
	else
	{
		marker9ball.SetActive( false );
		timer_running = false;
		fb_madepoint = false;
		fb_madefoul = false;
		sn_firsthit = 0;
		sn_secondhit = 0;
		sn_thirdhit = 0;
		
		// These dissapeared from v1.0.0 for some reason
		markerObj.SetActive( false );
		devhit.SetActive( false );
		guideline.SetActive( false );
	}

	_onlocal_updatescorecard();
}

string _netstr_hex()
{
	string str = "";

	for( int i = 0; i < net_data.Length; i += 2 )
	{
		ushort v = _decode_u16( i );
		str += v.ToString("X4");
	}

	return str;
}

// Wait for updates to the synced netstr
public override void OnDeserialization()
{
	if( !string.Equals( netstr, netstr_prv ) )
	{
		#if HT8B_DEBUGGER
		_frp( FRP_LOW + "OnDeserialization() :: netstr update" + FRP_END );
		#endif

		netstr_prv = netstr;

		// Check if local simulation is in progress, the event will fire off later when physics
		// are settled by the client
		if( sn_simulating )
		{
			#if HT8B_DEBUGGER
			_frp( FRP_WARN + "local simulation is still running, the network update will occur after completion" + FRP_END );
			#endif

			sn_updatelock = true;
		}
		else
		{
			// We are free to read this update
			_netread();
		}
	}
}

#endregion

#if !HT_QUEST

const int FRP_MAX = 32;
int FRP_LEN = 0;
int FRP_PTR = 0;
string[] FRP_LINES = new string[32];

// Print a line to the debugger
void _frp( string ln )
{
	Debug.Log( "[<color=\"#B5438F\">ht8b</color>] " + ln );

	FRP_LINES[ FRP_PTR ++ ] = "[<color=\"#B5438F\">ht8b</color>] " + ln + "\n";
	FRP_LEN ++ ;

	if( FRP_PTR >= FRP_MAX )
	{
		FRP_PTR = 0;
	}

	if( FRP_LEN > FRP_MAX )
	{
		FRP_LEN = FRP_MAX;
	}

	string output = "ht8b 1.0.0aa ";
		
	// Add information about game state:
	output += Networking.IsOwner(Networking.LocalPlayer, this.gameObject) ? 
		"<color=\"#95a2b8\">net(</color> <color=\"#4287F5\">OWNER</color> <color=\"#95a2b8\">)</color> ":
		"<color=\"#95a2b8\">net(</color> <color=\"#678AC2\">RECVR</color> <color=\"#95a2b8\">)</color> ";

	output += sn_simulating ?
		"<color=\"#95a2b8\">sim(</color> <color=\"#4287F5\">ACTIVE</color> <color=\"#95a2b8\">)</color> ":
		"<color=\"#95a2b8\">sim(</color> <color=\"#678AC2\">PAUSED</color> <color=\"#95a2b8\">)</color> ";

	VRCPlayerApi currentOwner = Networking.GetOwner( this.gameObject );
	output += "<color=\"#95a2b8\">player(</color> <color=\"#4287F5\">"+ (currentOwner != null? currentOwner.displayName: "[null]") + ":" + sn_turnid + "</color> <color=\"#95a2b8\">)</color>";

	output += "\n---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------\n";

	// Update display 
	for( int i = 0; i < FRP_LEN ; i ++ )
	{
		output += FRP_LINES[ (FRP_MAX + FRP_PTR - FRP_LEN + i) % FRP_MAX ];
	}

	ltext.text = output;
}

#endif

}
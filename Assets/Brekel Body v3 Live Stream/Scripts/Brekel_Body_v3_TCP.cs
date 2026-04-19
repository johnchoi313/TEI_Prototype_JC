using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using System.Collections.Generic;


enum Brekel_joint_name_v3
{
	waist = 0,
	spine,
	chest,
	neck,
	head,
	head_tip,

	upperLeg_L,
	lowerLeg_L,
	foot_L,
	toes_L,

	upperLeg_R,
	lowerLeg_R,
	foot_R,
	toes_R,

	collar_L,
	upperArm_L,
	foreArm_L,
	hand_L,
	fingersTip_L,

	collar_R,
	upperArm_R,
	foreArm_R,
	hand_R,
	fingersTip_R,

	numJoints
};

enum Brekel_blendshapes
{
	browDown_L = 0,
	browDown_R,
	browInnerUp_L,
	browInnerUp_R,
	browOuterUp_L,
	browOuterUp_R,
	cheekPuff_L,
	cheekPuff_R,
	cheekSquint_L,
	cheekSquint_R,
	eyeBlink_L,
	eyeBlink_R,
	eyeLookDown_L,
	eyeLookDown_R,
	eyeLookIn_L,
	eyeLookIn_R,
	eyeLookOut_L,
	eyeLookOut_R,
	eyeLookUp_L,
	eyeLookUp_R,
	eyeSquint_L,
	eyeSquint_R,
	eyeWide_L,
	eyeWide_R,
	jawForward,
	jawLeft,
	jawOpen,
	jawRight,
	mouthClose,
	mouthDimple_L,
	mouthDimple_R,
	mouthFrown_L,
	mouthFrown_R,
	mouthFunnel,
	mouthLeft,
	mouthLowerDown_L,
	mouthLowerDown_R,
	mouthPress_L,
	mouthPress_R,
	mouthPucker,
	mouthRight,
	mouthRollLower,
	mouthRollUpper,
	mouthShrugLower,
	mouthShrugUpper,
	mouthSmile_L,
	mouthSmile_R,
	mouthStretch_L,
	mouthStretch_R,
	mouthUpperUp_L,
	mouthUpperUp_R,
	noseSneer_L,
	noseSneer_R,
	numBlendshapes
}

[Serializable]
public class Brekel_joint_v3
{
	public	Transform	transform;
	public	float		confidence;
	public	Vector3		position;
	public	Quaternion	rotation;
}

[Serializable]
public class Brekel_skeleton_v3
{
	public Transform			reference;
	public float				timestamp;
	public Brekel_joint_v3[]	joints				= new Brekel_joint_v3[(int)Brekel_joint_name_v3.numJoints];
	public float[]				blendshape_weights	= new float[(int)Brekel_blendshapes.numBlendshapes];
}


public class Brekel_Body_v3_TCP : MonoBehaviour
{
	// settings
	[Header("Network Settings")]
	[Tooltip("Host name/IP that Brekel Body v3 is running on (localhost if it's running on the same machine)")]
	public	String						host					= "localhost";
	[Tooltip("Port that Brekel Body v3 is streaming on (8844 by default)")]
	public	int							port					= 8844;
	[Header("Mapping Settings")]
	[Tooltip("Body ID (from incoming Bodies array) to map to the transforms")]
	public	int							body_ID					= 0;
	[Tooltip("Apply positions to all (or just root) joints, when OFF can cause foot sliding")]
	public	bool						applyPositionsToAll		= true;
	[Tooltip("Transforms to map data to")]
	public	Transform					hips, spine, chest, neck, head, upperLeg_L, lowerLeg_L, foot_L, upperLeg_R, lowerLeg_R, foot_R, collar_L, upperArm_L, foreArm_L, hand_L, collar_R, upperArm_R, foreArm_R, hand_R;
	[Tooltip("Face mesh with skinned mesh renderer and blendshapes on it")]
	public	GameObject					face_mesh;
	[Header("Incoming Data")]
	[Tooltip("Received body data from the network stream")]
	private	const int					max_num_bodies			= 10;
	public	Brekel_skeleton_v3[]		bodies					= new Brekel_skeleton_v3[max_num_bodies];		// this stores all incoming body data

	// internal variables
	private	bool						isConnected				= false;
	private	TcpClient					client					= null;
	private	const int					readBuffer_size			= 65535;
	private	byte[]						readBuffer				= new byte[readBuffer_size];
	private	int							BytesRead				= 0;
	private	byte[]						packet_start			= new byte[6];
	private	byte[]						packet_end				= new byte[6];
	private	bool						readingFromNetwork		= false;
	private	Vector3						pos, rot;
	private	Quaternion[]				offsets					= new Quaternion[(int)Brekel_joint_name_v3.numJoints];
	private SkinnedMeshRenderer			face_skinned_renderer;
	

	//=============================
	// this is run once at startup
	//=============================
	void Start()
	{
		// get SkinnedMeshRenderers from face mesh
		if(face_mesh != null)
			face_skinned_renderer = face_mesh.GetComponent<SkinnedMeshRenderer>();

		StoreOffsets();	// store offsets of transforms
		Connect();		// connect to Brekel TCP network socket
	}


	//==========================
	// this is run once at exit
	//==========================
	void OnDisable()
	{
		Disconnect();	// disconnect from Brekel TCP network socket
	}


	//============================
	// this is run once per frame
	//============================
	void Update()
	{
		// keep bodyID sane (depending on the amount of bodies in the stream)
		if(body_ID < 0)					body_ID = 0;
		if(body_ID >= max_num_bodies)	body_ID = max_num_bodies-1;

		// only update if connected and currently not updating the data
		if(isConnected && !readingFromNetwork)
		{
			// apply data to transforms (must be done in Update, not in FetchFrame as that runs in it's own separate background thread)
			// a more sophisticated way or 'retargeting' would be to use inverse kinematics, this is beyond the scope of this example
			ApplyData_Transform(hips,		Brekel_joint_name_v3.waist,			true, true);
			ApplyData_Transform(spine,		Brekel_joint_name_v3.spine,			applyPositionsToAll, true);
			ApplyData_Transform(chest,		Brekel_joint_name_v3.chest,			applyPositionsToAll, true);
			ApplyData_Transform(neck,		Brekel_joint_name_v3.neck,			applyPositionsToAll, true);
			ApplyData_Transform(head,		Brekel_joint_name_v3.head,			applyPositionsToAll, true);
			ApplyData_Transform(upperLeg_L,	Brekel_joint_name_v3.upperLeg_L,	applyPositionsToAll, true);
			ApplyData_Transform(lowerLeg_L,	Brekel_joint_name_v3.lowerLeg_L,	applyPositionsToAll, true);
			ApplyData_Transform(foot_L,		Brekel_joint_name_v3.foot_L,		applyPositionsToAll, true);
			ApplyData_Transform(upperLeg_R,	Brekel_joint_name_v3.upperLeg_R,	applyPositionsToAll, true);
			ApplyData_Transform(lowerLeg_R,	Brekel_joint_name_v3.lowerLeg_R,	applyPositionsToAll, true);
			ApplyData_Transform(foot_R,		Brekel_joint_name_v3.foot_R,		applyPositionsToAll, true);
			ApplyData_Transform(collar_L,	Brekel_joint_name_v3.collar_L,		applyPositionsToAll, true);
			ApplyData_Transform(upperArm_L,	Brekel_joint_name_v3.upperArm_L,	applyPositionsToAll, true);
			ApplyData_Transform(foreArm_L,	Brekel_joint_name_v3.foreArm_L,		applyPositionsToAll, true);
			ApplyData_Transform(hand_L,		Brekel_joint_name_v3.hand_L,		applyPositionsToAll, true);
			ApplyData_Transform(collar_R,	Brekel_joint_name_v3.collar_R,		applyPositionsToAll, true);
			ApplyData_Transform(upperArm_R,	Brekel_joint_name_v3.upperArm_R,	applyPositionsToAll, true);
			ApplyData_Transform(foreArm_R,	Brekel_joint_name_v3.foreArm_R,		applyPositionsToAll, true);
			ApplyData_Transform(hand_R,		Brekel_joint_name_v3.hand_R,		applyPositionsToAll, true);

			// apply face blendshapes (we assume the correct number and order of shapes exist, for this example we don't do any custom remapping)
			ApplyData_Blendshapes(face_skinned_renderer);
		}
	}


	//==============================================
	// helper function to apply data to a transform
	//==============================================
	private void ApplyData_Transform(Transform transform, Brekel_joint_name_v3 joint, bool pos, bool rot)
	{
		if(!transform)
			return;
		if(pos)	transform.localPosition	= bodies[body_ID].joints[(int)joint].position;
		if(rot)	transform.localRotation	= offsets[(int)joint] * bodies[body_ID].joints[(int)joint].rotation;

	}


	//==============================================
	// helper function to apply data to a transform
	//==============================================
	private void ApplyData_Blendshapes(SkinnedMeshRenderer skinnedMeshRenderer)
	{
		if(skinnedMeshRenderer != null)
		{
			for(int i = 0; i < (int)Brekel_blendshapes.numBlendshapes; i++)
				skinnedMeshRenderer.SetBlendShapeWeight(i, bodies[body_ID].blendshape_weights[i] * 100);
		}
	}


	//=======================================================================
	// store offsets of the transforms so we can use them when applying data
	//=======================================================================
	private void StoreOffsets()
	{
		if(hips)		offsets[(int)Brekel_joint_name_v3.waist]		= Quaternion.Inverse(hips.localRotation);
		if(spine)		offsets[(int)Brekel_joint_name_v3.spine]		= Quaternion.Inverse(spine.localRotation);
		if(chest)		offsets[(int)Brekel_joint_name_v3.chest]		= Quaternion.Inverse(chest.localRotation);
		if(neck)		offsets[(int)Brekel_joint_name_v3.neck]			= Quaternion.Inverse(neck.localRotation);
		//if(head)		offsets[(int)Brekel_joint_name_v3.head]			= Quaternion.Inverse(head.localRotation);
		if(upperLeg_L)	offsets[(int)Brekel_joint_name_v3.upperLeg_L]	= Quaternion.Inverse(upperLeg_L.localRotation);
		if(lowerLeg_L)	offsets[(int)Brekel_joint_name_v3.lowerLeg_L]	= Quaternion.Inverse(lowerLeg_L.localRotation);
		if(foot_L)		offsets[(int)Brekel_joint_name_v3.foot_L]		= Quaternion.Inverse(foot_L.localRotation);
		if(upperLeg_R)	offsets[(int)Brekel_joint_name_v3.upperLeg_R]	= Quaternion.Inverse(upperLeg_R.localRotation);
		if(lowerLeg_R)	offsets[(int)Brekel_joint_name_v3.lowerLeg_R]	= Quaternion.Inverse(lowerLeg_R.localRotation);
		if(foot_R)		offsets[(int)Brekel_joint_name_v3.foot_R]		= Quaternion.Inverse(foot_R.localRotation);
		if(collar_L)	offsets[(int)Brekel_joint_name_v3.collar_L]		= Quaternion.Inverse(collar_L.localRotation);
		if(upperArm_L)	offsets[(int)Brekel_joint_name_v3.upperArm_L]	= Quaternion.Inverse(upperArm_L.localRotation);
		if(foreArm_L)	offsets[(int)Brekel_joint_name_v3.foreArm_L]	= Quaternion.Inverse(foreArm_L.localRotation);
		if(hand_L)		offsets[(int)Brekel_joint_name_v3.hand_L]		= Quaternion.Inverse(hand_L.localRotation);
		if(collar_R)	offsets[(int)Brekel_joint_name_v3.collar_R]		= Quaternion.Inverse(collar_R.localRotation);
		if(upperArm_R)	offsets[(int)Brekel_joint_name_v3.upperArm_R]	= Quaternion.Inverse(upperArm_R.localRotation);
		if(foreArm_R)	offsets[(int)Brekel_joint_name_v3.foreArm_R]	= Quaternion.Inverse(foreArm_R.localRotation);
		if(hand_R)		offsets[(int)Brekel_joint_name_v3.hand_R]		= Quaternion.Inverse(hand_R.localRotation);
	}
	

	//======================================
	// Connect to Brekel TCP network socket
	//======================================
	private bool Connect()
	{
		// try to connect to the Brekel Kinect Pro Body TCP network streaming port
		try
		{
			// instantiate new TcpClient
			client = new TcpClient(host, port);

			// Start an asynchronous read invoking DoRead to avoid lagging the user interface.
			client.GetStream().BeginRead(readBuffer, 0, readBuffer_size, new AsyncCallback(FetchFrame), null);

			Debug.Log("Connected to Brekel Body v3");
			return true;
		}
		catch (Exception ex)
		{
			Debug.Log("Error, can't connect to Brekel Body v3!" + ex.ToString());
			return false;
		}
	}


	//===========================================
	// Disconnect from Brekel TCP network socket
	//===========================================
	private void Disconnect()
	{
		if (client != null)
			client.Close();
		isConnected			= false;
		Debug.Log("Disconnected from Brekel Body v3");
	}


	//=====================================================================================
	// Fetch and parse data from the TCP network socket, asynchronous from the main thread
	//=====================================================================================
	private void FetchFrame(IAsyncResult ar)
	{
		//--------------------
		// try to read packet
		//--------------------
		try
		{
			// Finish asynchronous read into readBuffer and get number of bytes read.
			BytesRead = client.GetStream().EndRead(ar);

			// if no bytes were read server has closed
			if(BytesRead < 1)
			{
				Debug.Log("Brekel Body v3 Disconnected");
				isConnected = false;
				return;
			}

			// Convert the byte array the message was saved into
			Encoding.ASCII.GetString(readBuffer, 0, BytesRead);

			// check if packet start is valid
			System.Buffer.BlockCopy(readBuffer, 0,				packet_start, 0, 6);
			System.Buffer.BlockCopy(readBuffer, BytesRead-6,	packet_end,   0, 6);
			if(Encoding.UTF8.GetString(packet_start) != "BRKL_S" || Encoding.UTF8.GetString(packet_end) != "BRKL_E")
				Debug.Log("Invalid packet received from Brekel Body v3");
			else
			{
				int index					= 6 + 2;	// 6 for the packet_start, +2 for the encoded packet size which we don't need here since we can use BytesRead
				int num_joints				= BitConverter.ToInt32(readBuffer, index);	index += 4;
				int num_bodies				= BitConverter.ToInt32(readBuffer, index);	index += 4;
				int num_face_expressions	= BitConverter.ToInt32(readBuffer, index);	index += 4;
				if(num_bodies > max_num_bodies)
					num_bodies = max_num_bodies;

				// check if num_joints is what we expect it to be
				if(num_joints != (int)Brekel_joint_name_v3.numJoints)
					Debug.Log("Unexpected number of joints received from Brekel Body v3");
				else
				{
					// parse data
					for(int bodyID=0; bodyID < num_bodies; bodyID++)
					{
						bodies[bodyID].timestamp							= BitConverter.ToSingle(readBuffer, index);		index += 4;

						for(int jointID=0; jointID < num_joints; jointID++)
						{
							bodies[bodyID].joints[jointID].confidence		= BitConverter.ToSingle(readBuffer, index);		index += 4;
							pos.x											= BitConverter.ToSingle(readBuffer, index);		index += 4;
							pos.y											= BitConverter.ToSingle(readBuffer, index);		index += 4;
							pos.z											= BitConverter.ToSingle(readBuffer, index);		index += 4;
							rot.x											= BitConverter.ToSingle(readBuffer, index);		index += 4;
							rot.y											= BitConverter.ToSingle(readBuffer, index);		index += 4;
							rot.z											= BitConverter.ToSingle(readBuffer, index);		index += 4;
							bodies[bodyID].joints[jointID].position			= ConvertPosition(pos);
							bodies[bodyID].joints[jointID].rotation			= ConvertRotation(rot);
						}

						int actual_num_face_expressions	= BitConverter.ToInt32(readBuffer, index);	index += 4;
						if(actual_num_face_expressions > 0)
						{
							float quality = BitConverter.ToSingle(readBuffer, index);	index += 4;
							for(int exprID=0; exprID < actual_num_face_expressions; exprID++)
							{
								if(exprID >= (int)Brekel_blendshapes.numBlendshapes)
									break;

								bodies[bodyID].blendshape_weights[exprID] = BitConverter.ToSingle(readBuffer, index);	index += 4;
							}
						}
					}
				}
			}

			// done reading from network
			isConnected			= true;
			readingFromNetwork	= false;
		}


		//----------------------------------------------------------------
		// some non-critical error has occurred, possibly a broken packet
		//----------------------------------------------------------------
		catch
		{
			// do nothing
		}

		// Start a new asynchronous read into readBuffer.
		client.GetStream().BeginRead(readBuffer, 0, readBuffer_size, new AsyncCallback(FetchFrame), null);
	}


	//========================================================================================================================
	// Helper function to convert a position from a right-handed (Brekel/OpenGL) to left-handed coordinate system (both Y-up)
	//========================================================================================================================
	private Vector3 ConvertPosition(Vector3 position)
	{
		position.x *= -1;
		return position;
	}


	//========================================================================================================================
	// Helper function to convert a rotation from a right-handed (Brekel/OpenGL) to left-handed coordinate system (both Y-up)
	//========================================================================================================================
	private Quaternion ConvertRotation(Vector3 rotation)
	{
		Quaternion qx = Quaternion.AngleAxis(rotation.x, Vector3.right);
		Quaternion qy = Quaternion.AngleAxis(rotation.y, Vector3.down);
		Quaternion qz = Quaternion.AngleAxis(rotation.z, Vector3.back);
		Quaternion qq = qz * qy * qx;
		return qq;
	}
}

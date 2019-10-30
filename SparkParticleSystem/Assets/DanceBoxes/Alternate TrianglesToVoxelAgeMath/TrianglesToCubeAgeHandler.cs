﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DanceBoxes
{
	public class TrianglesToCubeAgeHandler : MonoBehaviour, IWantVertexPositions
	{
		public bool debug = false;

		public const int READ = 1;
		public const int WRITE = 0;

		public ComputeShader vertPosToCubeAgeCompute;

		public GameObject voxelAgeRecipientObject;
		IWantVoxelAges voxelAgeRecipient;


		ComputeBuffer[] triVertPosBuffers;
		ComputeBuffer rawVertexPositionARGSBuffer;

		ComputeBuffer[] filledVoxelGridBuffer = new ComputeBuffer[2];

		ComputeBuffer[] penDownVoxelBuffer = new ComputeBuffer[2];

		ComputeBuffer[] triangleIntersectionBuffer = new ComputeBuffer[2];
		ComputeBuffer triangleIntersectionARGSBuffer;

		int triangleCount;
		int vertexCount;
		public const string _intersectionsKernelName = "CSRunIntersections";
		public int intersectionsKernel
		{
			get
			{
				return vertPosToCubeAgeCompute.FindKernel(_intersectionsKernelName);
			}
		}

		public const string _intrsct2penPosKernelName = "CSIntersectionsToPenPos";
		public int intrsct2penPosKernel
		{
			get
			{
				return vertPosToCubeAgeCompute.FindKernel(_intrsct2penPosKernelName);
			}
		}

		public const string _penpos2VxlAgesKernelName = "CSPenposToVoxelAges"; 
		public int penpos2VxlAgesKernel
		{
			get
			{
				return vertPosToCubeAgeCompute.FindKernel(_penpos2VxlAgesKernelName);
			}
		}


		void Start()
		{
			filledVoxelGridBuffer[READ] = new ComputeBuffer(DanceBoxManager.inst.totalVoxels, DanceBoxManager.inst.sizeOfVoxelData, ComputeBufferType.Default);
			filledVoxelGridBuffer[WRITE] = new ComputeBuffer(DanceBoxManager.inst.totalVoxels, DanceBoxManager.inst.sizeOfVoxelData, ComputeBufferType.Default);

			penDownVoxelBuffer[WRITE] = new ComputeBuffer(DanceBoxManager.inst.totalVoxels, sizeof(float), ComputeBufferType.Default);
			penDownVoxelBuffer[READ] = new ComputeBuffer(DanceBoxManager.inst.totalVoxels, sizeof(float), ComputeBufferType.Default);

			triangleIntersectionARGSBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
			rawVertexPositionARGSBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

			voxelAgeRecipient = voxelAgeRecipientObject.GetComponent<IWantVoxelAges>();
			vertPosToCubeAgeCompute.SetVector("_Dimensions", DanceBoxManager.inst.voxelDimensions4);
			vertPosToCubeAgeCompute.SetVector("_InverseDimensions", new Vector4(1f/DanceBoxManager.inst.voxelDimensions.x, 1f / DanceBoxManager.inst.voxelDimensions.y, 1f / DanceBoxManager.inst.voxelDimensions.z, 1f / (DanceBoxManager.inst.voxelDimensions.x * DanceBoxManager.inst.voxelDimensions.y)));
		}


		void IWantVertexPositions.PassVertexPositions(ComputeBuffer[] parVertexPositionBuffers, int parVertexCount)
		{
			triVertPosBuffers = parVertexPositionBuffers;
			triangleCount = parVertexCount / 3;
			triangleIntersectionBuffer[READ] = new ComputeBuffer(triangleCount*(DanceBoxManager.inst.totalVoxels), DanceBoxManager.inst.sizeOfIntersectionData, ComputeBufferType.Append);
			triangleIntersectionBuffer[WRITE] = new ComputeBuffer(triangleCount*(DanceBoxManager.inst.totalVoxels), DanceBoxManager.inst.sizeOfIntersectionData, ComputeBufferType.Append);
			vertexCount = parVertexCount;
			vertPosToCubeAgeCompute.SetInt("_MaxCountVertexBuffer", vertexCount);
		}

		void Update()
		{
			BufferTools.Swap(filledVoxelGridBuffer);
			BufferTools.Swap(triangleIntersectionBuffer);
			BufferTools.Swap(penDownVoxelBuffer);

			if (triVertPosBuffers != null && triangleCount > 0)
			{
				DoCollisions();
			}
		}

		private void LateUpdate()
		{
		}


		void DoCollisions()
		{
			DoIntersections();
			DoPenDownCalculating();
			DoFillingVoxelGrid();
			voxelAgeRecipient.GiveSwappedVoxelAgeBuffer(filledVoxelGridBuffer[READ]);
		}




		void DoIntersections()
		{
			vertPosToCubeAgeCompute.SetBuffer(intersectionsKernel, "RTriangleVertexes", triVertPosBuffers[READ]);
			triangleIntersectionBuffer[WRITE].SetCounterValue(0);
			vertPosToCubeAgeCompute.SetBuffer(intersectionsKernel, "WAIntersections", triangleIntersectionBuffer[WRITE]);

			if (debug)
				BufferTools.DebugComputeRaw<Vector4>(triVertPosBuffers[READ], "unsorted vertex pos check", vertexCount);

			vertPosToCubeAgeCompute.Dispatch(intersectionsKernel, triangleCount, 1, 1);

		}

		void DoPenDownCalculating()
		{
			int[] args = BufferTools.GetArgs(triangleIntersectionBuffer[READ], triangleIntersectionARGSBuffer);
			//Debug.Log("numintersections: " + args[0]);
			vertPosToCubeAgeCompute.SetInt("IntersectionCount", args[0]);
			vertPosToCubeAgeCompute.SetBuffer(intrsct2penPosKernel, "RAIntersections", triangleIntersectionBuffer[READ]);
			vertPosToCubeAgeCompute.SetBuffer(intrsct2penPosKernel, "WPenPos", penDownVoxelBuffer[WRITE]);

			vertPosToCubeAgeCompute.Dispatch(intrsct2penPosKernel, (int)(DanceBoxManager.inst.totalVoxels), 1, 1);
			//if (debug)
			//	BufferTools.DebugComputeRaw<float>(filledVoxelGridBuffer[READ], " SHOULD HAVE TOTALINTERSECTION COUNTS IN ALL TINGS", (int)DanceBoxManager.inst.voxelDimensions.x);
		}

		void DoFillingVoxelGrid()
		{
			vertPosToCubeAgeCompute.SetBuffer(penpos2VxlAgesKernel, "RPenPos", penDownVoxelBuffer[READ]);
			vertPosToCubeAgeCompute.SetBuffer(penpos2VxlAgesKernel, "WVoxelAgeBuffer", filledVoxelGridBuffer[WRITE]);

			vertPosToCubeAgeCompute.Dispatch(penpos2VxlAgesKernel, (int)(DanceBoxManager.inst.voxelDimensions.x * DanceBoxManager.inst.voxelDimensions.y), 1, 1);
			//if (debug)
			//	BufferTools.DebugComputeRaw<float>(filledVoxelGridBuffer[READ], " SHOULD HAVE TOTALINTERSECTION COUNTS IN ALL TINGS", (int)DanceBoxManager.inst.voxelDimensions.x);
		}

		private void OnDisable()
		{
			filledVoxelGridBuffer[READ].Dispose();
			filledVoxelGridBuffer[WRITE].Dispose();
			triangleIntersectionBuffer[READ].Dispose();
			triangleIntersectionBuffer[WRITE].Dispose();
			penDownVoxelBuffer[READ].Dispose();
			penDownVoxelBuffer[WRITE].Dispose();
			//triangleBuffer.Dispose();
			triangleIntersectionARGSBuffer.Dispose();
			rawVertexPositionARGSBuffer.Dispose();
		}



	}

}

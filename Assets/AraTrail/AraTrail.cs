﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/**
 * Ara is a trail renderer for Unity, meant to replace and extend the standard TrailRenderer.
 */

namespace Ara{

    [ExecuteInEditMode]
    public class AraTrail : MonoBehaviour {
    
        public const float epsilon = 0.00001f;
    	
        public enum TrailAlignment{
            View,
            Velocity,
            Local
        }

        public enum TrailSorting{
            OlderOnTop,
            NewerOnTop
        }

        public enum Timescale{
            Normal,
            Unscaled
        }
    
        public enum TextureMode{
            Stretch,
            Tile
        }
    
        /**
         * Spatial frame, consisting of a point an three axis. This is used to implement the parallel transport method 
         * along the curve defined by the trail points. Using this instead of a Frenet-esque method avoids flipped frames
         * at points where the curvature changes.
         */
        public struct CurveFrame{  
    
            public Vector3 position;
            public Vector3 normal; 
            public Vector3 bitangent;
            public Vector3 tangent;
    
            public CurveFrame(Vector3 position, Vector3 normal,Vector3 bitangent,Vector3 tangent){
                this.position = position;
                this.normal = normal;
                this.bitangent = bitangent;
                this.tangent = tangent;
            }
    
            public Vector3 Transport(Vector3 newTangent, Vector3 newPosition){
    
                // double-reflection rotation-minimizing frame transport:
                Vector3 v1 = newPosition - position;
                float c1 = Vector3.Dot(v1,v1);
    
                Vector3 rL = normal - 2/(c1+epsilon) * Vector3.Dot(v1,normal) * v1;
                Vector3 tL = tangent - 2/(c1+epsilon) * Vector3.Dot(v1,tangent) * v1;
    
                Vector3 v2 = newTangent-tL;
                float c2 = Vector3.Dot(v2,v2);
                           
                Vector3 r1 = rL - 2/(c2+epsilon) * Vector3.Dot(v2,rL) * v2;
                Vector3 s1 = Vector3.Cross(newTangent,r1);
    
                normal = r1;
                bitangent = s1;
                tangent = newTangent;
                position = newPosition;
    
                return normal;
            }
        }
    
        /**
         * Holds information for each point in a trail: position, velocity and remaining lifetime. Points
         * can be added or subtracted, and interpolated using Catmull-Rom spline interpolation.
         */
    	public struct Point{
    
    		public Vector3 position;
            public Vector3 velocity;
            public Vector3 tangent;
            public Vector3 normal;
            public Color color;
            public float thickness;
    		public float life;
            public bool discontinuous;
    		
            public Point(Vector3 position,Vector3 velocity, Vector3 tangent, Vector3 normal, Color color, float thickness,float lifetime){
    			this.position = position;
                this.velocity = velocity;
                this.tangent = tangent;
                this.normal = normal;
                this.color = color;
                this.thickness = thickness;
    			this.life = lifetime;
                this.discontinuous = false;
    		}
    
            private static float CatmullRom(float p0, float p1, float p2, float p3, float t){
               float t2 = t*t;
               return 0.5f *((2 * p1) +
                             (-p0 + p2) * t +
                             (2*p0 - 5*p1 + 4*p2 - p3) * t2 +
                             (-p0 + 3*p1- 3*p2 + p3) * t2*t);
            }
    
            private static Color CatmullRomColor(Color p0, Color p1, Color p2, Color p3, float t){
                return new Color(CatmullRom(p0[0],p1[0],p2[0],p3[0],t),
                                 CatmullRom(p0[1],p1[1],p2[1],p3[1],t),
                                 CatmullRom(p0[2],p1[2],p2[2],p3[2],t),
                                 CatmullRom(p0[3],p1[3],p2[3],p3[3],t));
            }
    
            private static Vector3 CatmullRom3D(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t){
                return new Vector3(CatmullRom(p0[0],p1[0],p2[0],p3[0],t),
                                   CatmullRom(p0[1],p1[1],p2[1],p3[1],t),
                                   CatmullRom(p0[2],p1[2],p2[2],p3[2],t));
            }
    
            public static Point Interpolate(Point a, Point b, Point c, Point d, float t){
    
                return new Point( CatmullRom3D(a.position,
                                               b.position,
                                               c.position,
                                               d.position,t),
        
                                   CatmullRom3D(a.velocity,
                                                b.velocity,
                                                c.velocity,
                                                d.velocity,t),

                                    CatmullRom3D(a.tangent,
                                                 b.tangent,
                                                 c.tangent,
                                                 d.tangent,t),

                                    CatmullRom3D(a.normal,
                                                 b.normal,
                                                 c.normal,
                                                 d.normal,t),
    
                                   CatmullRomColor(a.color,
                                                   b.color,
                                                   c.color,
                                                   d.color,t),
    
                                   CatmullRom(a.thickness,
                                              b.thickness,
                                              c.thickness,
                                              d.thickness,t),
    
                                   CatmullRom(a.life,
                                              b.life,
                                              c.life,
                                              d.life,t)
                                );
            }
    
            public static Point operator +(Point p1, Point p2)
            {
                return new Point(p1.position + p2.position, 
                                 p1.velocity + p2.velocity,
                    p1.tangent + p2.tangent,
                    p1.normal + p2.normal,
                                 p1.color + p2.color,
                                 p1.thickness + p2.thickness,
                                 p1.life + p2.life);
            }
            public static Point operator -(Point p1, Point p2)
            {
                return new Point(p1.position - p2.position, 
                                 p1.velocity - p2.velocity,
                    p1.tangent - p2.tangent,
                    p1.normal - p2.normal,
                                 p1.color - p2.color,
                                 p1.thickness - p2.thickness,
                                 p1.life - p2.life);
            }
    	}

        [Header("Overall")]
        [Tooltip("Trail cross-section asset, determines the shape of the emitted trail. If no asset is specified, the trail will be a simple strip.")]
        public TrailSection section = null; 
        [Tooltip("Whether to use world or local space to generate and simulate the trail.")]
        public Space space = Space.World; 
        [Tooltip("Whether to use regular time.")]
        public Timescale timescale = Timescale.Normal; 
        [Tooltip("How to align the trail geometry: facing the camera (view) of using the transform's rotation (local).")]
        public TrailAlignment alignment = TrailAlignment.View;
        [Tooltip("Determines the order in which trail points will be rendered.")]
        public TrailSorting sorting = TrailSorting.OlderOnTop;
        [Tooltip("Thickness multiplier, in meters.")]
        public float thickness = 0.1f;
        [Tooltip("Amount of smoothing iterations applied to the trail shape.")]
        [Range(1,8)]
        public int smoothness = 1;
        [Tooltip("Calculate accurate thickness at sharp corners.")]
        public bool highQualityCorners = false;
        [Range(0,12)]
        public int cornerRoundness = 5;

        [Header("Lenght")]
    
        [Tooltip("How should the thickness of the curve evolve over its lenght. The horizontal axis is normalized lenght (in the [0,1] range) and the vertical axis is a thickness multiplier.")]
        public AnimationCurve thicknessOverLenght = AnimationCurve.Linear(0,1,0,1);    /**< maps trail length to thickness.*/
        [Tooltip("How should vertex color evolve over the trail's length.")]
        public Gradient colorOverLenght = new Gradient();

        [Header("Time")]
    
        [Tooltip("How should the thickness of the curve evolve with its lifetime. The horizontal axis is normalized lifetime (in the [0,1] range) and the vertical axis is a thickness multiplier.")]
        public AnimationCurve thicknessOverTime = AnimationCurve.Linear(0,1,0,1);  /**< maps trail lifetime to thickness.*/
        [Tooltip("How should vertex color evolve over the trail's lifetime.")]
        public Gradient colorOverTime = new Gradient();

        [Header("Emission")]
    
        public bool emit = true;
        [Tooltip("Initial thickness of trail points when they are first spawned.")]
        public float initialThickness = 1; /**< initial speed of trail, in world space. */
        [Tooltip("Initial color of trail points when they are first spawned.")]
        public Color initialColor = Color.white; /**< initial color of trail, in world space. */
        [Tooltip("Initial velocity of trail points when they are first spawned.")]
        public Vector3 initialVelocity = Vector3.zero; /**< initial speed of trail, in world space. */
        [Tooltip("Minimum amount of time (in seconds) that must pass before spawning a new point.")]
        public float timeInterval = 0.025f;
        [Tooltip("Minimum distance (in meters) that must be left between consecutive points in the trail.")]
        public float minDistance = 0.025f;
        [Tooltip("Duration of the trail (in seconds).")]
        public float time = 2f;

        [Header("Physics")]
    
        [Tooltip("Toggles trail physics.")]
        public bool enablePhysics = false;
        [Tooltip("Amount of seconds pre-simulated before the trail appears. Useful when you want a trail to be already simulating when the game starts.")]
        public float warmup = 0;               /**< simulation warmup seconds.*/
        [Tooltip("Gravity affecting the trail.")]
        public Vector3 gravity = Vector3.zero;  /**< gravity applied to the trail, in world space. */
        [Tooltip("Amount of speed transferred from the transform to the trail. 0 means no velocity is transferred, 1 means 100% of the velocity is transferred.")]
        [Range(0,1)]
        public float inertia = 0;               /**< amount of GameObject velocity transferred to the trail.*/
        [Tooltip("Amount of temporal smoothing applied to the velocity transferred from the transform to the trail.")]
        [Range(0,1)]
        public float velocitySmoothing = 0.75f;     /**< velocity smoothing amount.*/
        [Tooltip("Amount of damping applied to the trail's velocity. Larger values will slow down the trail more as time passes.")]
        [Range(0,1)]
        public float damping = 0.75f;               /**< velocity damping amount.*/

        [Header("Rendering")]

        public Material[] materials = new Material[]{null};
        public UnityEngine.Rendering.ShadowCastingMode castShadows = UnityEngine.Rendering.ShadowCastingMode.On;
        public bool receiveShadows = true;
        public bool useLightProbes = true;

        [Header("Texture")]
    
        [Tooltip("Quad mapping will send the shader an extra coordinate for each vertex, that can be used to correct UV distortion using tex2Dproj.")]
        public bool quadMapping = false;
        [Tooltip("How to apply the texture over the trail: stretch it all over its lenght, or tile it.")]
        public TextureMode textureMode = TextureMode.Stretch;
        [Tooltip("Defines how many times are U coords repeated across the length of the trail.")]
        public float uvFactor = 1;
        [Tooltip("Defines how many times are V coords repeated trough the width of the trail.")]
        public float uvWidthFactor = 1;
        [Tooltip("When the texture mode is set to 'Tile', defines where to begin tiling from: 0 means the start of the trail, 1 means the end.")]
        [Range(0,1)]
        public float tileAnchor = 1;
    
        public event System.Action onUpdatePoints;
    	
    	[HideInInspector] public List<Point> points = new List<Point>();
        private List<Point> renderablePoints = new List<Point>();
        private List<int> discontinuities = new List<int>();
    	
    	private Mesh mesh_;
        private Vector3 velocity = Vector3.zero;
        private Vector3 prevPosition;
        private float accumTime = 0;
    
    	private List<Vector3> vertices = new List<Vector3>();
    	private List<Vector3> normals = new List<Vector3>();
    	private List<Vector4> tangents = new List<Vector4>();
    	private List<Vector4> uvs = new List<Vector4>();
    	private List<Color> vertColors = new List<Color>();
    	private List<int> tris = new List<int>();

        public Vector3 Velocity{
            get{return velocity;}
        }

        private float DeltaTime{
            get{ return timescale == Timescale.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime;}
        }

        private float FixedDeltaTime{
            get{ return timescale == Timescale.Unscaled ? Time.fixedUnscaledDeltaTime : Time.fixedDeltaTime;}
        }

        public Mesh mesh{
            get{return mesh_;}
        }

        public void OnValidate(){
            time = Mathf.Max(time,epsilon);
            warmup = Mathf.Max(0,warmup);
        }

        public void Awake(){
            Warmup();
        }

#if (UNITY_2019_1_OR_NEWER)
        System.Action<ScriptableRenderContext, Camera> renderCallback;
#endif
        void OnEnable()
        {

            // initialize previous position, for correct velocity estimation in the first frame:
            prevPosition = transform.position;
            velocity = Vector3.zero;

            // create a new mesh for the trail:
            mesh_ = new Mesh();
            mesh_.name = "ara_trail_mesh";
            mesh_.MarkDynamic();

            AttachToCameraRendering();

        }

        void OnDisable()
        {

            // destroy both the trail mesh and the hidden renderer object:
            DestroyImmediate(mesh_);

            DetachFromCameraRendering();

        }

        private void AttachToCameraRendering()
        {
#if (UNITY_2019_1_OR_NEWER)
            // subscribe to OnPreCull for all cameras.
            renderCallback = new System.Action<ScriptableRenderContext, Camera>((cntxt, cam) => { UpdateTrailMesh(cam); });
            RenderPipelineManager.beginCameraRendering += renderCallback;
#endif
            Camera.onPreCull += UpdateTrailMesh;
        }

        private void DetachFromCameraRendering()
        {
            // unsubscribe from OnPreCull.
#if (UNITY_2019_1_OR_NEWER)
            RenderPipelineManager.beginCameraRendering -= renderCallback;
#endif
            Camera.onPreCull -= UpdateTrailMesh;
        }

        /**
         * Removes all points in the trail, effectively removing any rendered trail segments.
         */
        public void Clear(){
            points.Clear();
        }
    
    	private void UpdateVelocity(){
    
            if (DeltaTime > 0){
                velocity = Vector3.Lerp((transform.position - prevPosition) / DeltaTime,velocity,velocitySmoothing);
            }
    		prevPosition = transform.position;
    
    	}
    	
        /**
         * Updates point logic.
         */
    	private void LateUpdate () {
    
    		UpdateVelocity();
    		
    		EmissionStep(DeltaTime);
    
    		SnapLastPointToTransform();

            UpdatePointsLifecycle();

            if (onUpdatePoints != null)
                onUpdatePoints();
    	}

        private void EmissionStep(float time){
        
            // Acumulate the amount of time passed:
            accumTime += time;

            // If enough time has passed since the last emission (>= timeInterval), consider emitting new points.
            if (accumTime >= timeInterval){

                if (emit){

                    // Select the emission position, depending on the simulation space:
                    Vector3 position = space == Space.Self ? transform.localPosition : transform.position;
                    
                    // If there's at least 1 point and it is not far enough from the current position, don't spawn any new points this frame.
                    if (points.Count <= 1 || Vector3.Distance(position,points[points.Count-2].position) >= minDistance){
                        EmitPoint(position);    
                        accumTime = 0;
                    }
                }
            }

        }

        private void Warmup(){

            if (!Application.isPlaying || !enablePhysics)
                return;

            float simulatedTime = warmup;

            while (simulatedTime > FixedDeltaTime){

                PhysicsStep(FixedDeltaTime);

                EmissionStep(FixedDeltaTime);

                SnapLastPointToTransform();
    
                UpdatePointsLifecycle();

                if (onUpdatePoints != null)
                     onUpdatePoints();

                simulatedTime -= FixedDeltaTime;
            }
        }

        private void PhysicsStep(float timestep){

            float velocity_scale = Mathf.Pow(1-Mathf.Clamp01(damping),timestep);
    
            for (int i = 0; i < points.Count; ++i){
    
                Point point = points[i];
    
                // apply gravity and external forces:
                point.velocity += gravity * timestep;
                point.velocity *= velocity_scale;
    
                // integrate velocity:
                point.position += point.velocity * timestep;
    
                points[i] = point;
            }
        }
    
        /**
         * Updates point physics.
         */
        private void FixedUpdate(){
    
            if (!enablePhysics)
                return;
    
            PhysicsStep(FixedDeltaTime);
    
        }
    
        /**
         * Spawns a new point in the trail.
         */
        public void EmitPoint(Vector3 position){
            points.Add(new Point(position,initialVelocity + velocity * inertia,transform.right,transform.forward,initialColor,initialThickness,time));
    	}
   
        /**
         * Makes sure the first point is always at the transform's center, and that its orientation matches it.
         */
        private void SnapLastPointToTransform(){

            // Last point always coincides with transform:
            if (points.Count > 0){

                Point lastPoint = points[points.Count-1];

                // if we are not emitting, the last point is a discontinuity.
                if (!emit)
                    lastPoint.discontinuous = true;

                // if the point is discontinuous, move and orient it according to the transform.
                if (!lastPoint.discontinuous){
                    lastPoint.position = space == Space.Self ? transform.localPosition : transform.position;
                    lastPoint.normal = transform.forward;
                    lastPoint.tangent = transform.right;
                }

                points[points.Count-1] = lastPoint;
            }
        }

        /**
         * Updated trail lifetime and removes dead points.
         */
        private void UpdatePointsLifecycle(){

            for (int i = points.Count-1; i >= 0; --i){
    
                Point point = points[i];
                point.life -= DeltaTime;
                points[i] = point;
    
                if (point.life <= 0){

                    // Unsmoothed trails delete points as soon as they die.
                    if (smoothness <= 1){
                        points.RemoveAt(i);
                    }
                    // Smoothed trails however, should wait until the next 2 points are dead too. This ensures spline continuity.
                    else{
                        if (points[Mathf.Min(i+1,points.Count-1)].life <= 0 && 
                            points[Mathf.Min(i+2,points.Count-1)].life <= 0)
                            points.RemoveAt(i);
                    }

                }
            }
        }
    
        /**
         * Clears all mesh data: vertices, normals, tangents, etc. This is called at the beginning of UpdateTrailMesh().
         */
    	private void ClearMeshData(){
    
    		mesh_.Clear();
    		vertices.Clear();
    		normals.Clear();
    		tangents.Clear();
    		uvs.Clear();
    		vertColors.Clear();
    		tris.Clear();
    
    	}
    
        /**
         * Applies vertex, normal, tangent, etc. data to the mesh. Called at the end of UpdateTrailMesh()
         */
    	private void CommitMeshData(){
    
    		mesh_.SetVertices(vertices);
    		mesh_.SetNormals(normals);
    		mesh_.SetTangents(tangents);
    		mesh_.SetColors(vertColors);
    		mesh_.SetUVs(0,uvs);
    		mesh_.SetTriangles(tris,0,true);
    
    	}

        /** 
         * Asks Unity to render the trail mesh.
         */
        private void RenderMesh(Camera cam){
            for (int i = 0; i < materials.Length; ++i){
                Graphics.DrawMesh(mesh_,space == Space.Self && transform.parent != null ? transform.parent.localToWorldMatrix : Matrix4x4.identity,
                                  materials[i],gameObject.layer,cam,0,null,castShadows,receiveShadows,null,useLightProbes);
            }
        }
    
        /** 
         * Calculates the lenght of a trail segment.
         */
        public float GetLenght(List<Point> input){
    
            float lenght = 0;
    		for (int i = 0; i < input.Count-1; ++i)
    	        lenght += Vector3.Distance(input[i].position, input[i+1].position);
            return lenght;

    	}
    
        private List<Point> GetRenderablePoints(int start, int end){
    
            renderablePoints.Clear();    

            if (smoothness <= 1){
                for (int i = start; i <= end; ++i)
                    renderablePoints.Add(points[i]);
                return renderablePoints;
            }

            // calculate sample size in normalized coordinates:
            float samplesize = 1.0f / smoothness;
            
            for (int i = start; i < end; ++i){
                
                // Extrapolate first and last curve control points:
                Point firstPoint = i == start ? points[start] + (points[start] - points[i+1]) : points[i-1]; 
                Point lastPoint  = i == end-1 ? points[end]   + (points[end] - points[end-1]) : points[i+2]; 
    
                for (int j = 0; j < smoothness; ++j){
    
                    float t = j*samplesize;        
                    Point interpolated = Point.Interpolate(firstPoint,
                                                           points[i],
                                                           points[i+1],
                                                           lastPoint,t);
    
                    // only if the interpolated point is alive, we add it to the list of points to render.
                    if (interpolated.life > 0)
                        renderablePoints.Add(interpolated);
                }
                
            }
        
            if (points[end].life > 0)
                renderablePoints.Add(points[end]);

            return renderablePoints;
        }
    
        /**
         * Initializes the frame used to generate the locally aligned trail mesh.
         */
        private CurveFrame InitializeCurveFrame(Vector3 point, Vector3 nextPoint){

            Vector3 tangent = nextPoint-point;
    
            // Calculate tangent proximity to the normal vector of the frame (transform.forward).
            float tangentProximity = Mathf.Abs(Vector3.Dot(tangent.normalized,transform.forward));
    
            // If both vectors are dangerously close, skew the tangent a bit so that a proper frame can be formed:
            if (Mathf.Approximately(tangentProximity,1))
                tangent += transform.right * 0.01f;
    
            // Generate and return the frame:
            return new CurveFrame(point,transform.forward,transform.up,tangent);
        }
    
        /**
         * Updates the trail mesh to be seen from the camera passed to the function.
         */
    	private void UpdateTrailMesh(Camera cam){

            if ((cam.cullingMask & (1<<gameObject.layer)) == 0)
                return;
    
    		ClearMeshData();
    
            // We need at least two points to create a trail mesh.
            if (points.Count > 1){

                Vector3 localCamPosition = space == Space.Self && transform.parent != null ? transform.parent.InverseTransformPoint(cam.transform.position) : cam.transform.position;

                // get discontinuous point indices:
                discontinuities.Clear();
                for (int i = 0; i < points.Count; ++i)
                    if (points[i].discontinuous || i == points.Count-1) discontinuities.Add(i);

                // generate mesh for each trail segment:
                int start = 0;
                for (int i = 0 ; i < discontinuities.Count; ++i){
                    UpdateSegmentMesh(start,discontinuities[i],localCamPosition);
                    start = discontinuities[i]+1;
                }
                
        		CommitMeshData();

                RenderMesh(cam);
            }       
    	}

        /**
         * Updates mesh for one trail segment:
         */
        private void UpdateSegmentMesh(int start, int end, Vector3 localCamPosition){

            // Get a list of the actual points to render: either the original, unsmoothed points or the smoothed curve.
            List<Point> trail = GetRenderablePoints(start,end);

            if (sorting == TrailSorting.NewerOnTop)
                trail.Reverse();

            if (trail.Count > 1){
    
                float totalLength = Mathf.Max(GetLenght(trail),epsilon);
                float partialLength = 0;
                float vCoord = textureMode == TextureMode.Stretch ? 0 : - uvFactor * totalLength * tileAnchor;

                if (sorting == TrailSorting.NewerOnTop)
                    vCoord = 1 - vCoord;

                Vector4 texTangent = Vector4.zero;
                Vector4 uv = Vector4.zero;
                Color vertexColor;
    
                bool hqCorners = highQualityCorners && alignment != TrailAlignment.Local;

                // Initialize curve frame using the first two points to calculate the first tangent vector:
                CurveFrame frame = InitializeCurveFrame(trail[trail.Count-1].position,
                                                        trail[trail.Count-2].position);
                
                int va = 1;
                int vb = 0;
    
                for (int i = trail.Count-1; i >= 0; --i){
        
                    // Calculate next and previous point indices:
                    int nextIndex = Mathf.Max(i-1,0);
                    int prevIndex = Mathf.Min(i+1,trail.Count-1);

                    Point current = trail[i];
        
                    // Calculate next and previous trail vectors:
                    Vector3 nextV = trail[nextIndex].position - current.position;
                    Vector3 prevV = current.position - trail[prevIndex].position; 
                    float sectionLength = nextIndex == i ? prevV.magnitude : nextV.magnitude;

                    nextV.Normalize();
                    prevV.Normalize();

                    // Calculate tangent vector:
                    Vector3 tangent = alignment == TrailAlignment.Local ? current.tangent.normalized : (nextV + prevV)*0.5f;
            
                    // Calculate normal vector:
                    Vector3 normal = current.normal;
                    if (alignment != TrailAlignment.Local)
                        normal = alignment == TrailAlignment.View ? localCamPosition - current.position: frame.Transport(tangent,current.position);
                    normal.Normalize();
            
                    // Calculate bitangent vector:
                    Vector3 bitangent = alignment == TrailAlignment.Velocity ? frame.bitangent : Vector3.Cross(tangent,normal);
                    bitangent.Normalize();  
        
                    // Calculate this point's normalized (0,1) lenght and life.
                    float normalizedLength = sorting == TrailSorting.OlderOnTop ? partialLength / totalLength : (totalLength - partialLength) / totalLength;
                    float normalizedLife = Mathf.Clamp01(1 - current.life / time);
                    partialLength += sectionLength;

                    // Calulate vertex color:
                    vertexColor = current.color *
                                  colorOverTime.Evaluate(normalizedLife) * 
                                  colorOverLenght.Evaluate(normalizedLength);
        
                    // Calulate final thickness:
                    float sectionThickness = thickness * current.thickness * thicknessOverTime.Evaluate(normalizedLife) * thicknessOverLenght.Evaluate(normalizedLength);


                    if (section != null)
                    {

                        // Loop around each segment:
                        int sectionSegments = section.Segments;
                        int verticesPerSection = sectionSegments + 1;

                        for (int j = 0; j <= sectionSegments; ++j)
                        {

                            vertices.Add(current.position + (section.vertices[j].x * bitangent + section.vertices[j].y * tangent) * sectionThickness);
                            normals.Add(vertices[vertices.Count - 1] - current.position);
                            texTangent = -Vector3.Cross(normals[normals.Count - 1], frame.tangent);
                            texTangent.w = 1;
                            tangents.Add(texTangent);

                            vertColors.Add(vertexColor);

                            uv.Set((j / (float)sectionSegments) * uvWidthFactor, vCoord,0,1);
                            uvs.Add(uv);

                            if (j < sectionSegments && i < trail.Count - 1)
                            {

                                tris.Add(i * verticesPerSection + j);
                                tris.Add(i * verticesPerSection + (j + 1));
                                tris.Add((i + 1) * verticesPerSection + j);

                                tris.Add(i * verticesPerSection + (j + 1));
                                tris.Add((i + 1) * verticesPerSection + (j + 1));
                                tris.Add((i + 1) * verticesPerSection + j);

                            }
                        }

                    }
                    else
                    {

                        Quaternion q = Quaternion.identity;
                        Vector3 corner = Vector3.zero;
                        float curvatureSign = 0;
                        float correctedThickness = sectionThickness;
                        Vector3 prevSectionBitangent = bitangent;

                        // High-quality corners: 
                        if (hqCorners)
                        {

                            Vector3 nextSectionBitangent = i == 0 ? bitangent : Vector3.Cross(nextV, Vector3.Cross(bitangent, tangent)).normalized;

                            // If round corners are enabled:
                            if (cornerRoundness > 0)
                            {

                                prevSectionBitangent = i == trail.Count - 1 ? -bitangent : Vector3.Cross(prevV, Vector3.Cross(bitangent, tangent)).normalized;

                                // Calculate "elbow" angle:
                                curvatureSign = (i == 0 || i == trail.Count - 1) ? 1 : Mathf.Sign(Vector3.Dot(nextV, -prevSectionBitangent));
                                float angle = (i == 0 || i == trail.Count - 1) ? Mathf.PI : Mathf.Acos(Mathf.Clamp(Vector3.Dot(nextSectionBitangent, prevSectionBitangent), -1, 1));

                                // Prepare a quaternion for incremental rotation of the corner vector:
                                q = Quaternion.AngleAxis(Mathf.Rad2Deg * angle / cornerRoundness, normal * curvatureSign);
                                corner = prevSectionBitangent * sectionThickness * curvatureSign;
                            }

                            // Calculate correct thickness by projecting corner bitangent onto the next section bitangent. This prevents "squeezing"
                            if (nextSectionBitangent.sqrMagnitude > 0.1f)
                                correctedThickness = sectionThickness / Mathf.Max(Vector3.Dot(bitangent, nextSectionBitangent), 0.15f);

                        }


                        // Append straight section mesh data:
                        if (hqCorners && cornerRoundness > 0)
                        {

                            // bitangents are slightly asymmetrical in case of high-quality round or sharp corners:
                            if (curvatureSign > 0)
                            {
                                vertices.Add(current.position + prevSectionBitangent * sectionThickness);
                                vertices.Add(current.position - bitangent * correctedThickness);
                            }
                            else
                            {
                                vertices.Add(current.position + bitangent * correctedThickness);
                                vertices.Add(current.position - prevSectionBitangent * sectionThickness);
                            }

                        }
                        else
                        {
                            vertices.Add(current.position + bitangent * correctedThickness);
                            vertices.Add(current.position - bitangent * correctedThickness);
                        }

                        normals.Add(normal);
                        normals.Add(normal);

                        texTangent = tangent;
                        texTangent.w = 1;
                        tangents.Add(texTangent);
                        tangents.Add(texTangent);

                        vertColors.Add(vertexColor);
                        vertColors.Add(vertexColor);

                        if (quadMapping)
                        {
                            // passing perspective-correct coords requires the use of tex2Dproj in the shader, instead of tex2D.
                            uv.Set(vCoord * sectionThickness, sorting == TrailSorting.NewerOnTop ? uvWidthFactor * sectionThickness : 0,0,sectionThickness);
                            uvs.Add(uv);
                            uv.Set(vCoord * sectionThickness, sorting == TrailSorting.NewerOnTop ? 0 : uvWidthFactor * sectionThickness,0,sectionThickness);
                            uvs.Add(uv);
                        }
                        else
                        { 
                            uv.Set(vCoord, sorting == TrailSorting.NewerOnTop ? uvWidthFactor : 0,0,1);
                            uvs.Add(uv);
                            uv.Set(vCoord, sorting == TrailSorting.NewerOnTop ? 0 : uvWidthFactor,0,1);
                            uvs.Add(uv);
                        }

        
                        if (i < trail.Count-1){
    
                            int vc = vertices.Count - 1;
    
                            tris.Add(vc); 
                            tris.Add(va);
                            tris.Add(vb);  
    
                            tris.Add(vb); 
                            tris.Add(vc-1);  
                            tris.Add(vc);   
                        }
    
                        va = vertices.Count-1;
                        vb = vertices.Count-2;
    
                        // Append smooth corner mesh data:
                        if (hqCorners && cornerRoundness > 0){
    
                            for (int p = 0; p <= cornerRoundness; ++p){
        
                                vertices.Add(current.position + corner);
                                normals.Add(normal);
                                tangents.Add(texTangent);
                                vertColors.Add(vertexColor);
                                uv.Set(vCoord,curvatureSign > 0?0:1,0,1);
                                uvs.Add(uv);
        
                                int vc = vertices.Count - 1;
        
                                tris.Add(vc);
                                tris.Add(va);
                                tris.Add(vb); 
                               
                                if (curvatureSign > 0)
                                vb = vc; else va = vc;
        
                                // rotate corner point:
                                corner = q * corner;
                            }
    
                        }
                    }

                    // Update vcoord:
                    float uvDelta = (textureMode == TextureMode.Stretch ? sectionLength/totalLength : sectionLength);
                    vCoord += uvFactor * (sorting == TrailSorting.NewerOnTop ? -uvDelta : uvDelta);     
                        
                }
            }
            
        }
    }
}
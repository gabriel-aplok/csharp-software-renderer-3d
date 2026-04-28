using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SoftwareRenderer.ModelParser;

namespace SoftwareRenderer
{
    public class SoftBuffer(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
        public readonly int[] Pixels = new int[width * height];
        public readonly float[] DepthBuffer = new float[width * height];

        public void Clear(Color color)
        {
            int argb = color.ToArgb();
            Array.Fill(Pixels, argb);
            Array.Fill(DepthBuffer, float.MinValue);
        }
    }

    public class Renderer
    {
        public Matrix4x4 ModelMatrix;
        public Matrix4x4 ViewMatrix;
        public Matrix4x4 ProjectionMatrix;

        public void DrawModel(SoftBuffer buffer, IOBJHandle model, int color)
        {
            if (model.Indices.Length == 0)
                return;

            Matrix4x4 mvp = ModelMatrix * ViewMatrix * ProjectionMatrix;
            var vertices = model.Vertices;
            var indices = model.Indices;

            // the parser triangulated everything, so I can safely step by 3, or no????
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 v0 = vertices[indices[i]].Position;
                Vector3 v1 = vertices[indices[i + 1]].Position;
                Vector3 v2 = vertices[indices[i + 2]].Position;

                DrawTriangle(v0, v1, v2, color, mvp, buffer);
            }
        }

        public void DrawCube(SoftBuffer buffer)
        {
            // verts
            Vector3[] vertices =
            [
                new(-1, 1, 1),
                new(1, 1, 1),
                new(1, -1, 1),
                new(-1, -1, 1),
                new(-1, 1, -1),
                new(1, 1, -1),
                new(1, -1, -1),
                new(-1, -1, -1),
            ];

            // indices 12 triangles
            int[][] faces =
            [
                [0, 1, 2, 3, Color.Red.ToArgb()], // Front
                [1, 5, 6, 2, Color.Green.ToArgb()], // Right
                [5, 4, 7, 6, Color.Blue.ToArgb()], // Back
                [4, 0, 3, 7, Color.Yellow.ToArgb()], // Left
                [4, 5, 1, 0, Color.Orange.ToArgb()], // Top
                [3, 2, 6, 7, Color.Purple.ToArgb()], // Bottom
            ];

            Matrix4x4 mvp = ModelMatrix * ViewMatrix * ProjectionMatrix;

            foreach (var face in faces)
            {
                int color = face[4];
                // draw two triangles per face
                DrawTriangle(
                    vertices[face[0]],
                    vertices[face[1]],
                    vertices[face[2]],
                    color,
                    mvp,
                    buffer
                );
                DrawTriangle(
                    vertices[face[0]],
                    vertices[face[2]],
                    vertices[face[3]],
                    color,
                    mvp,
                    buffer
                );
            }
        }

        private static void DrawTriangle(
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            int color,
            Matrix4x4 mvp,
            SoftBuffer buffer
        )
        {
            // transform to clip space
            Vector4 p0 = Vector4.Transform(new Vector4(v0, 1.0f), mvp);
            Vector4 p1 = Vector4.Transform(new Vector4(v1, 1.0f), mvp);
            Vector4 p2 = Vector4.Transform(new Vector4(v2, 1.0f), mvp);

            // perspective divide & viewport transform
            Vector3 s0 = Project(p0, buffer.Width, buffer.Height);
            Vector3 s1 = Project(p1, buffer.Width, buffer.Height);
            Vector3 s2 = Project(p2, buffer.Width, buffer.Height);

            // back-face culling (simple cross product check)
            float edge = (s1.X - s0.X) * (s2.Y - s0.Y) - (s2.X - s0.X) * (s1.Y - s0.Y);
            if (edge >= 0)
                return;

            // bounding box
            int minX = (int)Math.Max(0, Math.Min(s0.X, Math.Min(s1.X, s2.X)));
            int maxX = (int)Math.Min(buffer.Width - 1, Math.Max(s0.X, Math.Max(s1.X, s2.X)));
            int minY = (int)Math.Max(0, Math.Min(s0.Y, Math.Min(s1.Y, s2.Y)));
            int maxY = (int)Math.Min(buffer.Height - 1, Math.Max(s0.Y, Math.Max(s1.Y, s2.Y)));

            float area = edge; // edge is proportional to triangle area btw

            // parallel rasterization per scanline
            Parallel.For(
                minY,
                maxY + 1,
                y =>
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        // barycentric coords
                        float w0 = ((s1.X - x) * (s2.Y - y) - (s2.X - x) * (s1.Y - y)) / area;
                        float w1 = ((s2.X - x) * (s0.Y - y) - (s0.X - x) * (s2.Y - y)) / area;
                        float w2 = 1.0f - w0 - w1;

                        if (w0 >= 0 && w1 >= 0 && w2 >= 0)
                        {
                            // interpolate z (depth)
                            float z = w0 * s0.Z + w1 * s1.Z + w2 * s2.Z;
                            int index = y * buffer.Width + x;

                            // z-buffer test
                            if (z > buffer.DepthBuffer[index])
                            {
                                buffer.DepthBuffer[index] = z;
                                buffer.Pixels[index] = color;
                            }
                        }
                    }
                }
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 Project(Vector4 v, int width, int height)
        {
            // perspective divide
            float wInv = 1.0f / v.W;
            return new Vector3(
                (v.X * wInv + 1.0f) * width * 0.5f,
                (1.0f - v.Y * wInv) * height * 0.5f,
                v.Z * wInv
            );
        }
    }

    public partial class MainForm : Form
    {
        private readonly SoftBuffer _buffer;
        private readonly Renderer _renderer;
        private float _rotation = 0;
        private readonly Bitmap _bitmap;
        private readonly System.Windows.Forms.Timer _timer;

        private readonly IOBJHandle _loadedModel;

        public MainForm()
        {
            Text = "Software Renderer, why not?";
            Width = 800;
            Height = 600;
            DoubleBuffered = true;

            string iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath))
                this.Icon = new Icon(iconPath);
            else
                Console.WriteLine($"Missing icon: {iconPath}");

            _buffer = new SoftBuffer(ClientSize.Width, ClientSize.Height);
            _renderer = new Renderer();
            _bitmap = new Bitmap(_buffer.Width, _buffer.Height, PixelFormat.Format32bppPArgb);

            _loadedModel = OBJFileLoader.CreateHandle();

            string modelPath = Path.Combine(AppContext.BaseDirectory, "res", "suzanne.obj");
            if (File.Exists(modelPath))
                OBJFileLoader.Load(_loadedModel, modelPath);
            else
                Console.WriteLine($"MissingSoftware Renderer, missing: {modelPath}");

            _timer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 FPS
            _timer.Tick += (s, e) =>
            {
                _rotation += 0.05f;
                Invalidate();
            };
            _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            _buffer.Clear(Color.Black);

            // update matrices
            float aspect = (float)_buffer.Width / _buffer.Height;
            _renderer.ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
                (float)Math.PI / 4,
                aspect,
                0.1f,
                100f
            );
            _renderer.ViewMatrix = Matrix4x4.CreateLookAt(
                new Vector3(0, 0, 7),
                Vector3.Zero,
                Vector3.UnitY
            );

            // draw the hardcoded cube
            _renderer.ModelMatrix =
                Matrix4x4.CreateRotationY(_rotation)
                * Matrix4x4.CreateRotationX(_rotation * 0.5f)
                * Matrix4x4.CreateTranslation(-2f, 0, 0);
            _renderer.DrawCube(_buffer);

            // draw the loaded model
            if (_loadedModel.Indices.Length > 0)
            {
                _renderer.ModelMatrix =
                    Matrix4x4.CreateRotationY(-_rotation) * Matrix4x4.CreateTranslation(2f, 0, 0);
                _renderer.DrawModel(_buffer, _loadedModel, Color.Cyan.ToArgb());
            }

            // blit to screen using lockbits
            BitmapData data = _bitmap.LockBits(
                new Rectangle(0, 0, _buffer.Width, _buffer.Height),
                ImageLockMode.WriteOnly,
                _bitmap.PixelFormat
            );
            Marshal.Copy(_buffer.Pixels, 0, data.Scan0, _buffer.Pixels.Length);
            _bitmap.UnlockBits(data);

            e.Graphics.DrawImage(_bitmap, 0, 0);
            e.Graphics.DrawRectangle(Pens.Red, 0, 0, _buffer.Width - 1, _buffer.Height - 1);
        }

        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}

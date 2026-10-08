using System;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTAClient.DXGUI.Bink
{
    public class BinkVideoPlayer : IDisposable
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly BinkDecoder decoder;
        private Texture2D texture;

        private double frameTimer;
        private bool playing;
        private string currentFile;

        public Texture2D CurrentTexture => texture;

        public bool IsPlaying => playing;
        public int Width => decoder.Width;
        public int Height => decoder.Height;

        public BinkVideoPlayer(GraphicsDevice graphicsDevice){
            this.graphicsDevice = graphicsDevice;

            decoder = new BinkDecoder();
        }

        public bool Play(string filename){
            Stop();

            Console.WriteLine($"BinkVideoPlayer: loading {filename}");

            if (!decoder.Open(filename)){
                Console.WriteLine("BinkVideoPlayer: failed to open video.");

                return false;
            }

            currentFile = filename;
            texture = new Texture2D(graphicsDevice,decoder.Width,decoder.Height,false,SurfaceFormat.Color);
            frameTimer = 0;
            playing = true;

            /*
             * Decode the first frame immediately.
             * This prevents the menu from displaying
             * a blank texture before the first Update().
             */
            if (!DecodeFrame()){
                Console.WriteLine("BinkVideoPlayer: failed to decode first frame.");

                Stop();

                return false;
            }

            Console.WriteLine("BinkVideoPlayer: playback started.");

            return true;
        }

        public void Update(GameTime gameTime)
        {
            if (!playing)
                return;

            if (!decoder.IsOpen){
                playing = false;
                return;
            }

            double frameDuration = 1.0 / decoder.FrameRate;

            frameTimer += gameTime.ElapsedGameTime.TotalSeconds;

            while (frameTimer >= frameDuration){
                frameTimer -= frameDuration;

                if (!DecodeFrame()){
                    Restart();

                    return;
                }
            }
        }

        private bool DecodeFrame(){
            byte[] pixels;

            if (!decoder.DecodeNextFrame(out pixels)){
                return false;
            }

            texture.SetData(pixels);
            return true;
        }

        private void Restart()
        {
            if (string.IsNullOrEmpty(currentFile)){
                playing = false;
                return;
            }

            Console.WriteLine("BinkVideoPlayer: looping video.");

            decoder.Close();

            if (!decoder.Open(currentFile)){
                Console.WriteLine("BinkVideoPlayer: failed to restart video.");

                playing = false;

                return;
            }

            frameTimer = 0;

            if (!DecodeFrame()){
                playing = false;
            }
        }

        public void Stop(){
            playing = false;
            frameTimer = 0;

            if (texture != null){
                texture.Dispose();
                texture = null;
            }

            decoder.Close();
            currentFile = null;
        }

        public void Dispose(){
            Stop();
            decoder.Dispose();
        }
    }
}
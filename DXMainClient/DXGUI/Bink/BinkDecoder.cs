using System;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;

namespace DTAClient.DXGUI.Bink
{
    public unsafe class BinkDecoder : IDisposable
    {
        private AVFormatContext* formatContext;
        private AVCodecContext* codecContext;
        private AVPacket* packet;
        private AVFrame* frame;
        private SwsContext* swsContext;

        private int videoStreamIndex = -1;

        private IntPtr convertedBuffer = IntPtr.Zero;

        private byte[] convertedPixels;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public double FrameRate { get; private set; } = 30.0;
        public bool IsOpen =>
            formatContext != null &&
            codecContext != null;

        public bool Open(string filename)
        {
            Close();

            try
            {
                Console.WriteLine($"Opening Bink video: {filename}");

                formatContext = ffmpeg.avformat_alloc_context();

                if (formatContext == null){Console.WriteLine("ERROR: Could not allocate AVFormatContext.");
                    return false;
                }

                AVFormatContext* openedFormatContext = formatContext;

                int result = ffmpeg.avformat_open_input(&openedFormatContext,filename,null,null);

                if (result < 0){
                    PrintError(
                        "avformat_open_input",
                        result
                    );

                    Close();
                    return false;
                }

                formatContext = openedFormatContext;

                result =ffmpeg.avformat_find_stream_info(formatContext,null);

                if (result < 0){
                    PrintError(
                        "avformat_find_stream_info",
                        result
                    );

                    Close();
                    return false;
                }

                AVCodec* codec = null;

                videoStreamIndex = ffmpeg.av_find_best_stream(formatContext,AVMediaType.AVMEDIA_TYPE_VIDEO,-1,-1,&codec,0);

                if (videoStreamIndex < 0 || codec == null)
                {
                    Console.WriteLine("ERROR: Could not find a video stream.");
                    Close();
                    return false;
                }

                string codecName = ffmpeg.avcodec_get_name(codec->id);

                Console.WriteLine($"Video codec: {codecName}");

                if (codec->id != AVCodecID.AV_CODEC_ID_BINKVIDEO) {
                    Console.WriteLine(
                        $"WARNING: Video codec is {codecName}, not Bink."
                    );
                }

                codecContext = ffmpeg.avcodec_alloc_context3(codec);

                if (codecContext == null) {
                    Console.WriteLine(
                        "ERROR: Could not allocate AVCodecContext."
                    );

                    Close();
                    return false;
                }

                AVStream* stream = formatContext->streams[videoStreamIndex];

                result = ffmpeg.avcodec_parameters_to_context(codecContext,stream->codecpar);

                if (result < 0){
                    PrintError(
                        "avcodec_parameters_to_context",
                        result
                    );

                    Close();
                    return false;
                }

                result = ffmpeg.avcodec_open2(codecContext,codec,null);

                if (result < 0)
                {
                    PrintError(
                        "avcodec_open2",
                        result
                    );

                    Close();
                    return false;
                }

                Width = codecContext->width;
                Height = codecContext->height;

                if (stream->avg_frame_rate.den != 0){
                    FrameRate = (double)stream->avg_frame_rate.num / stream->avg_frame_rate.den;
                }

                if (FrameRate <= 0 || double.IsNaN(FrameRate) || double.IsInfinity(FrameRate)){
                    FrameRate = 30.0;
                }

                /*
                 * Convert whatever pixel format Bink gives us
                 * into RGBA, which XNA SurfaceFormat.Color
                 * can consume on Windows/XNA.
                 */
                swsContext = ffmpeg.sws_getContext(
                                Width,
                                Height,
                                codecContext->pix_fmt,

                                Width,
                                Height,
                                AVPixelFormat.AV_PIX_FMT_RGBA,

                                (int)ffmpeg.SWS_FAST_BILINEAR,

                                null,
                                null,
                                null
                            );

                if (swsContext == null){
                    Console.WriteLine(
                        "ERROR: Could not create pixel conversion context."
                    );

                    Close();
                    return false;
                }

                int bufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_RGBA,Width,Height,1);

                if (bufferSize <= 0){
                    Console.WriteLine(
                        "ERROR: Invalid converted frame buffer size."
                    );

                    Close();
                    return false;
                }

                convertedBuffer = Marshal.AllocHGlobal(bufferSize);

                convertedPixels = new byte[bufferSize];

                packet = ffmpeg.av_packet_alloc();

                frame = ffmpeg.av_frame_alloc();

                if (packet == null || frame == null){
                    Console.WriteLine(
                        "ERROR: Could not allocate packet/frame."
                    );

                    Close();
                    return false;
                }

                Console.WriteLine(
                    $"Bink opened successfully: " +
                    $"{Width}x{Height}, " +
                    $"{FrameRate:F2} FPS"
                );

                return true;
            }
            catch (Exception ex){
                Console.WriteLine(
                    "BinkDecoder.Open failed:"
                );

                Console.WriteLine(ex);

                Close();

                return false;
            }
        }

        public bool DecodeNextFrame(out byte[] pixels){
            pixels = null;

            if (!IsOpen)
                return false;

            while (true){
                int result = ffmpeg.av_read_frame(formatContext,packet);

                if (result == ffmpeg.AVERROR_EOF){
                    return false;
                }

                if (result < 0){
                    PrintError("av_read_frame",result);

                    return false;
                }

                if (packet->stream_index !=
                    videoStreamIndex)
                {
                    ffmpeg.av_packet_unref(packet);

                    continue;
                }

                result = ffmpeg.avcodec_send_packet(codecContext,packet);

                ffmpeg.av_packet_unref(packet);

                if (result < 0){
                    PrintError("avcodec_send_packet",result);

                    return false;
                }

                result =ffmpeg.avcodec_receive_frame(codecContext,frame);

                if (result ==
                    ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    continue;
                }

                if (result ==ffmpeg.AVERROR_EOF){
                    return false;
                }

                if (result < 0){
                    PrintError("avcodec_receive_frame",result);

                    return false;
                }

                ConvertFrame();

                pixels = convertedPixels;

                return true;
            }
        }

        private void ConvertFrame()
        {
            byte* destinationData;
            int destinationLinesize;

            destinationData = (byte*)convertedBuffer;

            destinationLinesize = Width * 4;

            byte_ptrArray4 destination = new byte_ptrArray4();

            int_array4 destinationStride = new int_array4();

            destination[0] = destinationData;

            destinationStride[0] = destinationLinesize;

            ffmpeg.sws_scale(
                swsContext,

                frame->data,
                frame->linesize,

                0,
                frame->height,

                destination,
                destinationStride
            );

            Marshal.Copy(convertedBuffer,convertedPixels,0,convertedPixels.Length);
        }

        public void Close()
        {
            if (frame != null){
                AVFrame* tempFrame = frame;

                ffmpeg.av_frame_free(&tempFrame);

                frame = null;
            }

            if (packet != null){
                AVPacket* tempPacket = packet;

                ffmpeg.av_packet_free(&tempPacket);

                packet = null;
            }

            if (swsContext != null){
                ffmpeg.sws_freeContext(swsContext);

                swsContext = null;
            }

            if (convertedBuffer != IntPtr.Zero){
                Marshal.FreeHGlobal(convertedBuffer);

                convertedBuffer =IntPtr.Zero;
            }

            if (codecContext != null){
                AVCodecContext* tempCodecContext = codecContext;

                ffmpeg.avcodec_free_context(&tempCodecContext);

                codecContext = null;
            }

            if (formatContext != null){
                AVFormatContext* tempFormatContext = formatContext;

                ffmpeg.avformat_close_input(&tempFormatContext);

                formatContext = null;
            }

            videoStreamIndex = -1;

            Width = 0;
            Height = 0;

            convertedPixels = null;
        }

        private static void PrintError(string operation,int error){
            const int bufferSize = 1024;

            byte* buffer = stackalloc byte[bufferSize];

            ffmpeg.av_strerror(error,buffer,(ulong)bufferSize);

            string message =Marshal.PtrToStringAnsi((IntPtr)buffer);

            Console.WriteLine($"FFmpeg error in {operation}: " +$"{message} ({error})");
        }

        public void Dispose(){
            Close();
        }
    }
}
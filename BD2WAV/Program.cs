using NAudio.Wave;

namespace BD2WAV
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string bdPath = string.Empty;
            string hdPath = string.Empty;
            bool providedSample = false;
            int sampleRate = 32000;
            for (int i = 0; i < args.Length; i++)
            {
                if (int.TryParse(args[i], out int sample))
                {
                    sampleRate = sample;
                    providedSample = true;
                }
                else
                {
                    if (bdPath == string.Empty)
                        bdPath = args[i];
                    else
                        hdPath = args[i];
                }
            }

            if (bdPath == string.Empty)
                Info();

            Console.WriteLine("\nBD2WAV\n");

            if (!File.Exists(bdPath))
            {
                Console.WriteLine("Input file doesn't exist: " + bdPath + "!!!");
                Quit();
            }

            if (!providedSample && hdPath == string.Empty)
            {
                string? path = Path.GetDirectoryName(bdPath);
                if (path == null)
                    path = Directory.GetCurrentDirectory();
                else if (!path.Contains(":") && path.Contains("\\\\"))
                    path = Path.Combine(path, Directory.GetCurrentDirectory());
                path = Path.Combine(path, Path.GetFileNameWithoutExtension(bdPath) + ".hd");

                if (File.Exists(path))
                {
                    hdPath = path;
                    Console.WriteLine($"Found hd file: {hdPath}");
                }
            }

            List<int> sampleRates = new List<int>();

            if (!providedSample && hdPath != string.Empty)
            {
                if (!File.Exists(hdPath))
                {
                    Console.WriteLine("hd file: " + hdPath + " doesn't exist!");
                }
                else
                {
                    bool foundData = false;
                    Console.WriteLine("Reading hd file: " + hdPath + "...");
                    using (BinaryReader reader = new BinaryReader(File.OpenRead(hdPath)))
                    {
                        while (reader.BaseStream.Position < reader.BaseStream.Length)
                        {
                            reader.BaseStream.Position += 4;
                            long start = reader.BaseStream.Position;
                            uint chunkId = reader.ReadUInt32();
                            if (chunkId != 0x56616769) //igaV/Vagi (VagInfo)
                                continue;
                            foundData = true;
                            reader.BaseStream.Position += 4;
                            int maxIndex = reader.ReadInt32();
                            int infoCount = maxIndex+1;
                            for (int i = 0; i < infoCount; i++)
                            {
                                int infoOffset = reader.ReadInt32();
                                long current = reader.BaseStream.Position;
                                reader.BaseStream.Position = start + infoOffset;
                                sampleRates.Add(reader.ReadUInt16());
                                reader.BaseStream.Position = current;
                            }
                            break;
                        }
                    }
                    if (!foundData)
                    {
                        Console.WriteLine("Could not find any sample rate info in hd file!");
                    }
                }
            }
            if (!providedSample && hdPath == string.Empty)
            {
                Console.WriteLine($"Using default sample rate: {sampleRate}!!!");
            }

            List<byte[]> sampleData = new List<byte[]>();

            Console.WriteLine("Reading bd file: " + bdPath + "...");
            using (BinaryReader reader = new BinaryReader(File.OpenRead(bdPath)))
            {
                List<byte> currentFile = new List<byte>();
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    byte[] curLine = reader.ReadBytes(16);
                    if (Array.TrueForAll(curLine, x => x == 0))
                    {
                        if (currentFile.Count > 0)
                        {
                            sampleData.Add(currentFile.ToArray());
                            currentFile.Clear();
                        }
                        continue;
                    }
                    currentFile.AddRange(curLine);
                }
                sampleData.Add(currentFile.ToArray());
            }

            if (sampleRates.Count == 0)
            {
                Console.WriteLine($"Using sample rate: {sampleRate}");
                for (int i = 0; i < sampleData.Count; i++)
                    sampleRates.Add(sampleRate);
            }
            else if (sampleRates.Count < sampleData.Count)
            {
                Console.WriteLine($"Too little sample rate data in the hd file (wrong hd file?), expanding data with last entry: {sampleRates.Last()}");
                for (int i = 0; i < sampleData.Count - sampleRates.Count; i++) 
                    sampleRates.Add(sampleRates.Last());
            }

            string? dir = Path.GetDirectoryName(bdPath);
            if (dir == null)
                dir = Directory.GetCurrentDirectory();
            else if (!dir.Contains(":") && dir.Contains("\\\\"))
                dir = Path.Combine(dir, Directory.GetCurrentDirectory());
            dir = Path.Combine(dir, Path.GetFileNameWithoutExtension(bdPath));
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            int digits = sampleData.Count.ToString().Length;

            for (int i = 0; i < sampleData.Count; i++)
            {
                IWaveProvider provider = new RawSourceWaveStream(new MemoryStream(Decode(sampleData[i])), new WaveFormat(sampleRates[i], 16,1));
                WaveFileWriter.CreateWaveFile(Path.Combine(dir, i.ToString($"D{digits}") + ".wav"), provider);
            }
            Console.WriteLine($"Saved {sampleData.Count} wav files to {dir}");
        }
        static void Info()
        {
            Console.WriteLine("BD2WAV");
            Console.WriteLine("A program for converting audio data contained in ps2 bd files to wav.");
            Console.WriteLine("\nThis program will convert a bd file given to it as parameter.");
            Console.WriteLine("If there is an hd file with the same file name as the bd file");
            Console.WriteLine("the sample rate for each audio clip will be taken from that file.");
            Console.WriteLine("If the bd file has a different file name/location you can input it as a second parameter.");
            Console.WriteLine("If you know the sample rate you can simply specify it as an argument instead of using an hd file.");
            Console.WriteLine("(The default sample rate is 32000)");
            Console.WriteLine("\nUsage:");
            Console.WriteLine("\tBD2WAV.exe input.bd");
            Console.WriteLine("\tBD2WAV.exe input.bd input.hd");
            Console.WriteLine("\tBD2WAV.exe input.bd 22050(sampleRate)");
            Quit();
        }
        static void Quit()
        {
            Console.WriteLine("\n\nPress any key to exit");
            Console.ReadKey();
            Environment.Exit(0);
        }
        private static readonly double[,] Coefficients = new double[,]
        {
            {0.0, 0.0},
            {60.0 / 64.0, 0.0},
            {115.0 / 64.0, -52.0 / 64.0},
            {98.0 / 64.0, -55.0 / 64.0},
            {122.0 / 64.0, -60.0 / 64.0}
        };
        public static byte[] Decode(byte[] inputData)
        {
            byte[] decodedData = new byte[inputData.Length / 16 * 28 * 2];
            using (BinaryReader reader = new BinaryReader(new MemoryStream(inputData)))
            {
                int index = 0;
                double hist1 = 0;
                double hist2 = 0;
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    byte coefficent = reader.ReadByte();
                    byte flags = reader.ReadByte();
                    byte[] sampleData = reader.ReadBytes(14);

                    sbyte shift = (sbyte)(coefficent & 0xF);
                    sbyte predict = (sbyte)((coefficent & 0xF0) >> 4);

                    if (flags == 7) //End flag
                        break;

                    int[] samples = new int[28];

                    for (int j = 0; j < 14; j++)
                    {
                        samples[j * 2] = sampleData[j] & 0xF;
                        samples[j * 2 + 1] = (sampleData[j] & 0xF0) >> 4;
                    }

                    for (int j = 0; j < 28; j++)
                    {
                        int sample = samples[j] << 12;
                        if ((sample & 0x8000) != 0)
                            sample = (int)(sample | 0xFFFF0000);

                        predict = Math.Min(predict, (sbyte)(Coefficients.GetLength(0) - 1));

                        hist2 = hist1;
                        hist1 = (sample >> shift) + hist1 * Coefficients[predict, 0] + hist2 * Coefficients[predict, 1];

                        byte[] sampleBytes = BitConverter.GetBytes((short)Math.Clamp(hist1, short.MinValue, short.MaxValue));
                        decodedData[index++] = sampleBytes[0];
                        decodedData[index++] = sampleBytes[1];
                    }
                }
            }

            return decodedData;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EditMap2
{

    public class IniFile : VirtualTextFile
    {

        public List<IniSection> Sections { get; set; }
        public IniSection CurrentSection { get; set; }

        //static Logger logger = LogManager.GetCurrentClassLogger();

        public IniFile(Stream baseStream, string filename, int baseOffset, long fileSize, bool isBuffered = true)
            : base(baseStream, filename, baseOffset, fileSize, isBuffered)
        {
            Sections = new List<IniSection>();
            Parse();
        }

        public IniSection GetSection(string sectionName)
        {
            return Sections.Find(x => x.Name == sectionName);
        }

        public IniSection GetOrCreateSection(string sectionName, string insertAfter = null)
        {
            var ret = Sections.Find(x => x.Name == sectionName);
            if (ret == null)
            {
                int insertIdx = (insertAfter != null) ? Sections.FindIndex(section => section.Name == insertAfter) : -1;

                ret = new IniSection(sectionName);
                if (insertIdx != -1)
                {
                    Sections.Insert(insertIdx, ret);
                    ret.Index = insertIdx;
                    // move up all section indices
                    for (int i = insertIdx + 1; i < Sections.Count; i++)
                        Sections[i].Index++;
                }
                else
                {
                    Sections.Add(ret);
                    ret.Index = Sections.Count;
                }
            }
            return ret;
        }

        void Parse()
        {//TODO
            //logger.Info("Parsing {0}", Path.GetFileName(FileName));
            while (CanRead)
            {
                ProcessLine(ReadLine());
            }
            // support for Ares tag
            var includes = GetOrCreateSection("#include");
            //foreach (var entry in includes.OrderedEntries)
            //  MergeWith(VFS.Open<IniFile>(entry.Value));
        }

        int ProcessLine(string line)
        {
            IniSection.FixLine(ref line);
            if (line.Length == 0) return 0;

            // Test if this line contains start of new section i.e. matches [*]
            if ((line[0] == '[') && (line[line.Length - 1] == ']'))
            {
                string sectionName = line.Substring(1, line.Length - 2);
                var iniSection = new IniSection(sectionName, Sections.Count);
                //logger.Trace("Loading ini section {0}", sectionName);
                Sections.Add(iniSection);
                CurrentSection = iniSection;
            }
            else if (CurrentSection != null)
            {
                return CurrentSection.ParseLine(line);
            }
            return 0;
        }

        void SetCurrentSection(string sectionName)
        {
            //logger.Trace("Changing current section to {0}", sectionName);
            CurrentSection = Sections.Find(x => x.Name == sectionName);
        }

        public void SetCurrentSection(IniSection section)
        {
            if (Sections.Contains(section))
                CurrentSection = section;
            else
                throw new InvalidOperationException("Invalid section");
        }

        public string ReadString(string section, string key, string @default = "")
        {
            if (CurrentSection == null || CurrentSection.Name != section)
                SetCurrentSection(section);
            return CurrentSection.ReadString(key, @default);
        }

        public bool ReadBool(string key)
        {
            return CurrentSection.ReadBool(key);
        }

        public bool ReadBool(string section, string key)
        {
            if (CurrentSection.Name != section)
                SetCurrentSection(section);
            return ReadBool(key);
        }

        public class IniSection
        {//GetSection的定义来源
            public int Index { get; set; }
            public string Name { get; set; }

            public class IniValue
            {
                private string value;
                public IniValue(string value)
                {
                    this.value = value;
                }

                public override string ToString()
                {
                    return value;
                }
                public static implicit operator IniValue(string value)
                {
                    return new IniValue(value);
                }
                public static implicit operator string(IniValue val)
                {
                    return val.value;
                }
                public void Set(string value)
                {
                    this.value = value;
                }
                public override bool Equals(object obj)
                {
                    return value.Equals(obj.ToString());
                }
                protected bool Equals(IniValue other)
                {
                    return string.Equals(value, other.value);
                }

                public override int GetHashCode()
                {
                    return (value != null ? value.GetHashCode() : 0);
                }

            }

            public Dictionary<string, IniValue> SortedEntries { get; set; }
            public List<KeyValuePair<string, IniValue>> OrderedEntries { get; set; }

            //static NumberFormatInfo culture = CultureInfo.InvariantCulture.NumberFormat;

            public IniSection(string name = "", int index = -1)
            {
                SortedEntries = new Dictionary<string, IniValue>();
                OrderedEntries = new List<KeyValuePair<string, IniValue>>();
                Name = name;
                Index = index;
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append('[');
                sb.Append(Name);
                sb.AppendLine("]");
                foreach (var v in OrderedEntries)
                {
                    sb.Append(v.Key);
                    sb.Append('=');
                    sb.AppendLine(v.Value);
                }
                return sb.ToString();
            }

            public void Clear()
            {
                SortedEntries.Clear();
                OrderedEntries.Clear();
            }

            public int ParseLines(IEnumerable<string> lines)
            {
                return lines.Sum(line => ParseLine(line));
            }

            public int ParseLine(string line)
            {
                // ignore comments
                if (line[0] == ';') return 0;
                string key;
                int pos = line.IndexOf("=", StringComparison.Ordinal);
                if (pos != -1)
                {
                    key = line.Substring(0, pos);
                    string value = line.Substring(pos + 1);
                    FixLine(ref key);
                    FixLine(ref value);
                    SetValue(key, value, false);
                    return 1;
                }
                return 0;
            }

            public void SetValue(string key, string value, bool @override = true)
            {
                if (!SortedEntries.ContainsKey(key))
                {
                    IniValue val = value;
                    OrderedEntries.Add(new KeyValuePair<string, IniValue>(key, val));
                    SortedEntries[key] = val;
                }
                else if (@override)
                {
                    SortedEntries[key].Set(value);
                    OrderedEntries.RemoveAll(e => e.Key == key);
                    OrderedEntries.Add(new KeyValuePair<string, IniValue>(key, value));
                }
            }

            public static void FixLine(ref string line)
            {
                int start = 0;

                while (start < line.Length && (line[start] == ' ' || line[start] == '\t'))
                    start++;

                int end = line.IndexOf(';', start);
                if (end == -1) end = line.Length;

                while (end > 1 && (line[end - 1] == ' ' || line[end - 1] == '\t'))
                    end--;

                line = line.Substring(start, Math.Max(end - start, 0));
            }

            public static string FixLine(string line)
            {
                string copy = line;
                FixLine(ref copy);
                return copy;
            }

            public bool HasKey(string keyName)
            {
                return SortedEntries.ContainsKey(keyName);
            }

            static readonly string[] TrueValues = { "yes", "1", "true", "on" };
            static readonly string[] FalseValues = { "no", "0", "false", "off" };

            public bool ReadBool(string key, bool defaultValue = false)
            {
                string entry = ReadString(key);
                if (TrueValues.Contains(entry, StringComparer.InvariantCultureIgnoreCase))
                    return true;
                else if (FalseValues.Contains(entry, StringComparer.InvariantCultureIgnoreCase))
                    return false;
                else return defaultValue;
            }

            public string ReadString(string key, string defaultValue = "")
            {
                IniValue ret;
                if (SortedEntries.TryGetValue(key, out ret))
                    return ret;
                else
                    return defaultValue;
            }

            public int ReadInt(string key, int defaultValue = 0)
            {
                int ret;
                if (int.TryParse(ReadString(key), out ret))
                    return ret;
                else
                    return defaultValue;
            }
            /*
            public Point ReadXY(string key)
            {
                string[] val = ReadString(key).Split(',');
                return new Point(int.Parse(val[0]), int.Parse(val[1]));
            }

            public short ReadShort(string key, short defaultValue = 0)
            {
                short ret;
                if (short.TryParse(ReadString(key), out ret))
                    return ret;
                else
                    return defaultValue;
            }
            */
            /*
            public float ReadFloat(string key, float defaultValue = 0.0f)
            {
                float ret;
                if (float.TryParse(ReadString(key).Replace(',', '.'), NumberStyles.Any, culture, out ret))
                    return ret;
                else
                    return defaultValue;
            }

            public double ReadDouble(string key, double defaultValue = 0.0)
            {
                double ret;
                if (double.TryParse(ReadString(key).Replace(',', '.'), NumberStyles.Any, culture, out ret))
                    return ret;
                else
                    return defaultValue;
            }
            */
            /*public Color ReadColor(string key)
            {
                string colorStr = ReadString(key, "0,0,0");
                string[] colorParts = colorStr.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries);

                int r, g, b;
                if (colorParts.Length == 3 && int.TryParse(colorParts[0], out r) && int.TryParse(colorParts[0], out g) && int.TryParse(colorParts[0], out b))
                    return Color.FromArgb(r, g, b);

                KnownColor known;
                if (KnownColor.TryParse(colorStr, true, out known))
                    return Color.FromKnownColor(known);

                return Color.Empty;
            }
            */
            public T ReadEnum<T>(string key, T @default)
            {
                if (HasKey(key))
                    return (T)Enum.Parse(typeof(T), ReadString(key));
                return @default;
            }

            public List<string> ReadList(string key)
            {
                return ReadString(key).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            public string ConcatenatedValues()
            {
                var sb = new StringBuilder();
                foreach (var v in OrderedEntries)
                    sb.Append(v.Value);
                return sb.ToString();
            }

            /// <summary>
            ///  returns index of key:value
            /// </summary>
            /// <param name="p"></param>
            /// <returns></returns>
            public int FindValueIndex(string p)
            {
                for (int i = 0; i < OrderedEntries.Count; i++)
                    if (OrderedEntries[i].Value == p)
                        return i;
                return -1;
            }

            public void WriteTo(StreamWriter sw)
            {
                sw.Write('[');
                sw.Write(Name);
                sw.WriteLine(']');
                foreach (var kvp in OrderedEntries)
                {
                    sw.Write(kvp.Key);
                    sw.Write('=');
                    sw.WriteLine(kvp.Value);
                }
            }

            /*public Vector3 ReadXYZ(string key)
            {
                return ReadXYZ(key, new Vector3(0, 0, 0));//TODO
            }
            public Vector3 ReadXYZ(string key, Vector3 @default)
            {
                string size = ReadString(key);
                string[] parts = size.Split(',');
                int x, y, z;
                if (int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y) && int.TryParse(parts[2], out z))
                    return new Vector3(x, y, z);
                return @default;
            }
            */
            /*
            public Size ReadSize(string key)
            {
                return ReadSize(key, new Size(0, 0));
            }
            */
            /*
            public Size ReadSize(string key, Size @default)
            {
                string size = ReadString(key);
                string[] parts = size.Split(',');
                int x, y;
                if (int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y))
                    return new Size(x, y);
                return @default;
            }

            public Point ReadPoint(string key)
            {
                return ReadPoint(key, Point.Empty);
            }
            public Point ReadPoint(string key, Point @default)
            {
                string point = ReadString(key);
                string[] parts = point.Split(',');
                int x, y;
                if (int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y))
                    return new Point(x, y);
                return @default;
            }
            */
        }

        public void Save(string filename)
        {
            var sw = new StreamWriter(filename, false, Encoding.Default, 64 * 1024);
            foreach (var section in Sections)
            {
                if (section.Name == "#include" && section.OrderedEntries.Count == 0)
                    continue;
                section.WriteTo(sw);
                if (section != Sections.Last())
                    sw.WriteLine();
            }
            sw.Flush();
            sw.Dispose();
        }

        /// <summary>
        /// Merges (and overrides) the entries from given ini files with this
        /// </summary>
        /// <param name="ini"></param>
        public void MergeWith(IniFile ini)
        {
            if (ini == null) return;

            foreach (var v in ini.Sections)
            {
                var ownSection = GetOrCreateSection(v.Name);
                // numbered arrays are 'appended' instead of overwritten
                if (IsObjectArray(v.Name))
                {
                    try
                    {
                        int number = 1 + int.Parse(ownSection.OrderedEntries.Last().Key);
                        foreach (var kvp in v.OrderedEntries)
                            ownSection.SetValue(number++.ToString(), kvp.Value);
                    }
                    catch (FormatException)
                    {
                        foreach (var kvp in v.OrderedEntries)
                            ownSection.SetValue(kvp.Key, kvp.Value);
                    }
                }
                else
                    foreach (var kvp in v.OrderedEntries)
                        ownSection.SetValue(kvp.Key, kvp.Value);
            }
        }

        private bool IsObjectArray(string p)
        {//是否是这几类
            return new[] {
                "BuildingTypes",
                "AircraftTypes",
                "InfantryTypes",
                "OverlayTypes",
                "TerrainTypes",
                "SmudgeTypes",
                "VehicleTypes",
            }.Contains(p);
        }

    }
    public class VirtualTextFile : VirtualFile
    {
        public VirtualTextFile(Stream file, string filename = "")
            : base(file, filename, true)
        {
            Position = 0;
        }

        public VirtualTextFile(Stream file, string filename, int baseOffset, long length, bool isBuffered = true)
            : base(file, filename, baseOffset, length, isBuffered)
        {
            Position = 0;
        }

        public override bool CanRead
        {
            get { return !Eof; }
        }

        public virtual string ReadLine()
        {
            // works for ascii only!
            var builder = new StringBuilder(80);
            while (CanRead)
            {
                char c = (char)ReadByte();
                if (c == '\n')
                    break;
                else if (c != '\r')
                    builder.Append(c);
            }
            return builder.ToString();
        }

    }
    /// <summary>
    /// Virtual file class
    /// </summary>
    /// 
    /// <summary>Virtual file from a memory buffer.</summary>
    /// 

    public class MemoryFile : VirtualFile
    {//定义见VirtualFile.cs,它的基类是Stream

        public MemoryFile(byte[] buffer, bool isBuffered = true) :
            base(new MemoryStream(buffer), "MemoryFile", 0, buffer.Length, isBuffered)
        { }// //TODO Calling the base class method?
           //MemoryStream:Creates a stream whose backing store is memory.
           //base是什么函数
    }
    public class VirtualFile : Stream
    {//Stream是一个抽象类。有写入、读取、查找三个功能
        public Stream BaseStream { get; internal protected set; }//TODO Returns the underlying stream.
        protected int BaseOffset;
        protected long Size;
        protected long Pos;
        virtual public string FileName { get; set; }
        //In C#, for overriding the base class method in a derived class, you have to declare a base class method as virtual and derived class method asoverride:

        byte[] _buff;
        readonly bool _isBuffered;
        bool _isBufferInitialized;

        public VirtualFile(Stream baseStream, string filename, int baseOffset, long fileSize, bool isBuffered = false)
        {
            Size = fileSize;
            BaseOffset = baseOffset;
            BaseStream = baseStream;
            _isBuffered = isBuffered;
            FileName = filename;
        }

        public VirtualFile(Stream baseStream, string filename = "", bool isBuffered = false)
        {//重载
            BaseStream = baseStream;
            BaseOffset = 0;
            Size = baseStream.Length;
            _isBuffered = isBuffered;
            FileName = filename;
        }

        public override bool CanRead
        {
            get { return Pos < Size; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override long Length
        {
            get { return Size; }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            count = Math.Min(count, (int)(Length - Position));
            if (_isBuffered)
            {
                if (!_isBufferInitialized)
                    InitBuffer();

                Array.Copy(_buff, Pos, buffer, offset, count);
            }
            else
            {
                // ensure
                BaseStream.Position = BaseOffset + Pos;
                BaseStream.Read(buffer, offset, count);
            }
            Pos += count;
            return count;
        }

        public string ReadCString(int count)
        {
            var arr = Read(count);
            var sb = new StringBuilder();
            int i = 0;
            while (i < count && arr[i] != 0)
                sb.Append((char)arr[i++]);
            return sb.ToString();
        }

        public unsafe int Read(byte* buffer, int count)
        {
            count = Math.Min(count, (int)(Length - Position));
            if (_isBuffered)
            {
                if (!_isBufferInitialized)
                    InitBuffer();

                for (int i = 0; i < count; i++)
                    *buffer++ = _buff[Pos + i];
            }
            else
            {
                // ensure
                BaseStream.Position = BaseOffset + Pos;
                byte[] rbuff = new byte[count];
                BaseStream.Read(rbuff, 0, count);
                for (int i = 0; i < count; i++)
                    *buffer++ = rbuff[i];
            }
            Pos += count;
            return count;
        }

        private void InitBuffer()
        {
            // ensure
            BaseStream.Position = BaseOffset + Pos;
            _buff = new byte[Size];
            BaseStream.Read(_buff, 0, (int)Size);
            _isBufferInitialized = true;
        }

        public byte[] Read(int numBytes)
        {//read的定义
            var ret = new byte[numBytes];//TODO ret是一个numBytes长度的bytes数组，那它为什么作为参数输入到read里
            Read(ret, 0, numBytes);//public abstract int Read (byte[] buffer, int offset, int count);
                                   //Parameters
                                   //buffer Byte[]
                                   //An array of bytes. When this method returns, the buffer contains the specified byte array with the values between offset and (offset + count - 1) replaced by the bytes read from the current source.

            //offset Int32
            //The zero-based byte offset in buffer at which to begin storing the data read from the current stream.

            //count Int32
            //The maximum number of bytes to be read from the current stream.

            //Returns Int32
            //The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.
            return ret;
        }

        public sbyte[] ReadSigned(int numBytes)
        {
            var b = new byte[numBytes];
            Read(b, 0, numBytes);
            sbyte[] ret = new sbyte[numBytes];
            Buffer.BlockCopy(b, 0, ret, 0, b.Length);
            return ret;
        }

        public new byte ReadByte()
        {
            return ReadUInt8();
        }

        public sbyte ReadSByte()
        {
            return unchecked((sbyte)ReadUInt8());
        }

        public byte ReadUInt8()
        {
            return Read(1)[0];
        }

        public int ReadInt32()
        {
            return BitConverter.ToInt32(Read(sizeof(Int32)), 0);
        }

        public uint ReadUInt32()
        {
            return BitConverter.ToUInt32(Read(sizeof(UInt32)), 0);
        }

        public short ReadInt16()
        {
            return BitConverter.ToInt16(Read(sizeof(Int16)), 0);
        }

        public ushort ReadUInt16()
        {
            return BitConverter.ToUInt16(Read(sizeof(UInt16)), 0);
        }

        public float ReadFloat()
        {
            return BitConverter.ToSingle(Read(sizeof(Single)), 0);
        }

        public float ReadFloat2()
        {
            var ori = Read(sizeof(Single)).ToList();
            byte[] rev = new[] { ori[3], ori[2], ori[1], ori[0] };
            return BitConverter.ToSingle(rev, 0);
        }

        public double ReadDouble()
        {
            return BitConverter.ToDouble(Read(sizeof(Double)), 0);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Close()
        {
            base.Close();
            BaseStream.Close();
        }

        public override void SetLength(long value)
        {
            Size = value;
        }

        public override long Position
        {
            get
            {
                return Pos;
            }
            set
            {
                Pos = value;
                if (!_isBuffered && Pos + BaseOffset != BaseStream.Position)
                    BaseStream.Seek(Pos + BaseOffset, SeekOrigin.Begin);
            }
        }

        public long Remaining
        {
            get { return Length - Pos; }
        }

        public bool Eof
        {
            get { return Remaining <= 0; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.End:
                    Position = Length - offset;
                    break;
            }
            return Position;
        }

        public override string ToString()
        {
            return FileName;
        }
    }
    public static class Format80
    {
        private static void ReplicatePrevious(byte[] dest, int destIndex, int srcIndex, int count)
        {
            if (srcIndex > destIndex)
                throw new NotImplementedException(string.Format("srcIndex > destIndex  {0}  {1}", srcIndex, destIndex));

            if (destIndex - srcIndex == 1)
            {
                for (int i = 0; i < count; i++)
                    dest[destIndex + i] = dest[destIndex - 1];
            }
            else
            {
                for (int i = 0; i < count; i++)
                    dest[destIndex + i] = dest[srcIndex + i];
            }
        }

        public static int DecodeInto(byte[] src, byte[] dest)
        {
            VirtualFile ctx = new MemoryFile(src);
            int destIndex = 0;

            while (true)
            {
                byte i = ctx.ReadByte();
                if ((i & 0x80) == 0)
                {
                    // case 2
                    byte secondByte = ctx.ReadByte();
                    int count = ((i & 0x70) >> 4) + 3;
                    int rpos = ((i & 0xf) << 8) + secondByte;

                    ReplicatePrevious(dest, destIndex, destIndex - rpos, count);
                    destIndex += count;
                }
                else if ((i & 0x40) == 0)
                {
                    // case 1
                    int count = i & 0x3F;
                    if (count == 0)
                        return destIndex;

                    ctx.Read(dest, destIndex, count);
                    destIndex += count;
                }
                else
                {
                    int count3 = i & 0x3F;
                    if (count3 == 0x3E)
                    {
                        // case 4
                        int count = ctx.ReadInt16();
                        byte color = ctx.ReadByte();

                        for (int end = destIndex + count; destIndex < end; destIndex++)
                            dest[destIndex] = color;
                    }
                    else if (count3 == 0x3F)
                    {
                        // case 5
                        int count = ctx.ReadInt16();
                        int srcIndex = ctx.ReadInt16();
                        if (srcIndex >= destIndex)
                            throw new NotImplementedException(string.Format("srcIndex >= destIndex  {0}  {1}", srcIndex, destIndex));

                        for (int end = destIndex + count; destIndex < end; destIndex++)
                            dest[destIndex] = dest[srcIndex++];
                    }
                    else
                    {
                        // case 3
                        int count = count3 + 3;
                        int srcIndex = ctx.ReadInt16();
                        if (srcIndex >= destIndex)
                            throw new NotImplementedException(string.Format("srcIndex >= destIndex  {0}  {1}", srcIndex, destIndex));

                        for (int end = destIndex + count; destIndex < end; destIndex++)
                            dest[destIndex] = dest[srcIndex++];
                    }
                }
            }
        }

        public static unsafe uint DecodeInto(byte* src, byte* dest)
        {
            byte* pdest = dest;
            byte* readp = src;
            byte* writep = dest;

            while (true)
            {
                byte code = *readp++;
                byte* copyp;
                int count;
                if ((~code & 0x80) != 0)
                {
                    //bit 7 = 0
                    //command 0 (0cccpppp p): copy
                    count = (code >> 4) + 3;
                    copyp = writep - (((code & 0xf) << 8) + *readp++);
                    while (count-- != 0)
                        *writep++ = *copyp++;
                }
                else
                {
                    //bit 7 = 1
                    count = code & 0x3f;
                    if ((~code & 0x40) != 0)
                    {
                        //bit 6 = 0
                        if (count == 0)
                            //end of image
                            break;
                        //command 1 (10cccccc): copy
                        while (count-- != 0)
                            *writep++ = *readp++;
                    }
                    else
                    {
                        //bit 6 = 1
                        if (count < 0x3e)
                        {
                            //command 2 (11cccccc p p): copy
                            count += 3;
                            copyp = &pdest[*(ushort*)readp];

                            readp += 2;
                            while (count-- != 0)
                                *writep++ = *copyp++;
                        }
                        else if (count == 0x3e)
                        {
                            //command 3 (11111110 c c v): fill
                            count = *(ushort*)readp;
                            readp += 2;
                            code = *readp++;
                            while (count-- != 0)
                                *writep++ = code;
                        }
                        else
                        {
                            //command 4 (copy 11111111 c c p p): copy
                            count = *(ushort*)readp;
                            readp += 2;
                            copyp = &pdest[*(ushort*)readp];
                            readp += 2;
                            while (count-- != 0)
                                *writep++ = *copyp++;
                        }
                    }
                }
            }

            return (uint)(dest - pdest);
        }

        public static byte[] Encode(byte[] src)
        {
            /* quick & dirty format80 encoder -- only uses raw copy operator, terminated with a zero-run. */
            /* this does not produce good compression, but it's valid format80 */
            var ctx = new MemoryFile(src);
            var ms = new MemoryStream();

            do
            {
                var len = Math.Min(ctx.Position, 0x3F);
                ms.WriteByte((byte)(0x80 | len));
                while (len-- > 0)
                    ms.WriteByte(ctx.ReadByte());
            } while (!ctx.Eof);

            ms.WriteByte(0x80); // terminator -- 0-length run.

            return ms.ToArray();
        }
    }
    public static class MiniLZO
    {

        unsafe static uint lzo1x_1_compress_core(byte* @in, uint in_len, byte* @out, ref uint out_len, uint ti, void* wrkmem)
        {
            byte* ip;
            byte* op;
            byte* in_end = @in + in_len;
            byte* ip_end = @in + in_len - 20;
            byte* ii;
            ushort* dict = (ushort*)wrkmem;
            op = @out;
            ip = @in;
            ii = ip;
            ip += ti < 4 ? 4 - ti : 0;

            byte* m_pos;
            uint m_off;
            uint m_len;

            for (; ; )
            {

                uint dv;
                uint dindex;
            literal:
                ip += 1 + ((ip - ii) >> 5);
            next:
                if (ip >= ip_end)
                    break;
                dv = (*(uint*)(void*)(ip));
                dindex = ((uint)(((((((uint)((0x1824429d) * (dv)))) >> (32 - 14))) & (((1u << (14)) - 1) >> (0))) << (0)));
                m_pos = @in + dict[dindex];
                dict[dindex] = ((ushort)((uint)((ip) - (@in))));
                if (dv != (*(uint*)(void*)(m_pos)))
                    goto literal;

                ii -= ti; ti = 0;
                {
                    uint t = ((uint)((ip) - (ii)));
                    if (t != 0)
                    {
                        if (t <= 3)
                        {
                            op[-2] |= ((byte)(t));
                            *(uint*)(op) = *(uint*)(ii);
                            op += t;
                        }
                        else if (t <= 16)
                        {
                            *op++ = ((byte)(t - 3));
                            *(uint*)(op) = *(uint*)(ii);
                            *(uint*)(op + 4) = *(uint*)(ii + 4);
                            *(uint*)(op + 8) = *(uint*)(ii + 8);
                            *(uint*)(op + 12) = *(uint*)(ii + 12);
                            op += t;
                        }
                        else
                        {
                            if (t <= 18)
                                *op++ = ((byte)(t - 3));
                            else
                            {
                                uint tt = t - 18;
                                *op++ = 0;
                                while (tt > 255)
                                {
                                    tt -= 255;
                                    *(byte*)op++ = 0;
                                }

                                *op++ = ((byte)(tt));
                            }
                            do
                            {
                                *(uint*)(op) = *(uint*)(ii);
                                *(uint*)(op + 4) = *(uint*)(ii + 4);
                                *(uint*)(op + 8) = *(uint*)(ii + 8);
                                *(uint*)(op + 12) = *(uint*)(ii + 12);
                                op += 16; ii += 16; t -= 16;
                            } while (t >= 16); if (t > 0) { do *op++ = *ii++; while (--t > 0); }
                        }
                    }
                }
                m_len = 4;
                {
                    uint v;
                    v = (*(uint*)(void*)(ip + m_len)) ^ (*(uint*)(void*)(m_pos + m_len));
                    if (v == 0)
                    {
                        do
                        {
                            m_len += 4;
                            v = (*(uint*)(void*)(ip + m_len)) ^ (*(uint*)(void*)(m_pos + m_len));
                            if (ip + m_len >= ip_end)
                                goto m_len_done;
                        } while (v == 0);
                    }
                    m_len += (uint)lzo_bitops_ctz32(v) / 8;
                }
            m_len_done:
                m_off = ((uint)((ip) - (m_pos)));
                ip += m_len;
                ii = ip;
                if (m_len <= 8 && m_off <= 0x0800)
                {
                    m_off -= 1;
                    *op++ = ((byte)(((m_len - 1) << 5) | ((m_off & 7) << 2)));
                    *op++ = ((byte)(m_off >> 3));
                }
                else if (m_off <= 0x4000)
                {
                    m_off -= 1;
                    if (m_len <= 33)
                        *op++ = ((byte)(32 | (m_len - 2)));
                    else
                    {
                        m_len -= 33;
                        *op++ = 32 | 0;
                        while (m_len > 255)
                        {
                            m_len -= 255;
                            *(byte*)op++ = 0;
                        }
                        *op++ = ((byte)(m_len));
                    }
                    *op++ = ((byte)(m_off << 2));
                    *op++ = ((byte)(m_off >> 6));
                }
                else
                {
                    m_off -= 0x4000;
                    if (m_len <= 9)
                        *op++ = ((byte)(16 | ((m_off >> 11) & 8) | (m_len - 2)));
                    else
                    {
                        m_len -= 9;
                        *op++ = ((byte)(16 | ((m_off >> 11) & 8)));
                        while (m_len > 255)
                        {
                            m_len -= 255;
                            *(byte*)op++ = 0;
                        }
                        *op++ = ((byte)(m_len));
                    }
                    *op++ = ((byte)(m_off << 2));
                    *op++ = ((byte)(m_off >> 6));
                }
                goto next;
            }
            out_len = ((uint)((op) - (@out)));
            return ((uint)((in_end) - (ii - ti)));
        }

        static int[] MultiplyDeBruijnBitPosition = {
              0, 1, 28, 2, 29, 14, 24, 3, 30, 22, 20, 15, 25, 17, 4, 8,
              31, 27, 13, 23, 21, 19, 16, 7, 26, 12, 18, 6, 11, 5, 10, 9
            };
        private static int lzo_bitops_ctz32(uint v)
        {
            return MultiplyDeBruijnBitPosition[((uint)((v & -v) * 0x077CB531U)) >> 27];
        }

        unsafe static int lzo1x_1_compress(byte* @in, uint in_len, byte* @out, ref uint out_len, byte* wrkmem)
        {
            byte* ip = @in;
            byte* op = @out;
            uint l = in_len;
            uint t = 0;
            while (l > 20)
            {
                uint ll = l;
                ulong ll_end;
                ll = ((ll) <= (49152) ? (ll) : (49152));
                ll_end = (ulong)ip + ll;
                if ((ll_end + ((t + ll) >> 5)) <= ll_end || (byte*)(ll_end + ((t + ll) >> 5)) <= ip + ll)
                    break;

                for (int i = 0; i < (1 << 14) * sizeof(ushort); i++)
                    wrkmem[i] = 0;
                t = lzo1x_1_compress_core(ip, ll, op, ref out_len, t, wrkmem);
                ip += ll;
                op += out_len;
                l -= ll;
            }
            t += l;
            if (t > 0)
            {
                byte* ii = @in + in_len - t;
                if (op == @out && t <= 238)
                    *op++ = ((byte)(17 + t));
                else if (t <= 3)
                    op[-2] |= ((byte)(t));
                else if (t <= 18)
                    *op++ = ((byte)(t - 3));
                else
                {
                    uint tt = t - 18;
                    *op++ = 0;
                    while (tt > 255)
                    {
                        tt -= 255;
                        *(byte*)op++ = 0;
                    }

                    *op++ = ((byte)(tt));
                }
                do *op++ = *ii++; while (--t > 0);
            }
            *op++ = 16 | 1;
            *op++ = 0;
            *op++ = 0;
            out_len = ((uint)((op) - (@out)));
            return 0;
        }

        public unsafe static int lzo1x_decompress(byte* @in, uint in_len, byte* @out, ref uint out_len, void* wrkmem)
        {
            byte* op;
            byte* ip;
            uint t;
            byte* m_pos;
            byte* ip_end = @in + in_len;
            out_len = 0;
            op = @out;
            ip = @in;
            bool gt_first_literal_run = false;
            bool gt_match_done = false;
            if (*ip > 17)
            {
                t = (uint)(*ip++ - 17);
                if (t < 4)
                {
                    match_next(ref op, ref ip, ref t);
                }
                else
                {
                    do *op++ = *ip++; while (--t > 0);
                    gt_first_literal_run = true;
                }
            }
            while (true)
            {
                if (gt_first_literal_run)
                {
                    gt_first_literal_run = false;
                    goto first_literal_run;
                }

                t = *ip++;
                if (t >= 16)
                    goto match;
                if (t == 0)
                {
                    while (*ip == 0)
                    {
                        t += 255;
                        ip++;
                    }
                    t += (uint)(15 + *ip++);
                }
                *(uint*)op = *(uint*)ip;
                op += 4; ip += 4;
                if (--t > 0)
                {
                    if (t >= 4)
                    {
                        do
                        {
                            *(uint*)op = *(uint*)ip;
                            op += 4; ip += 4; t -= 4;
                        } while (t >= 4);
                        if (t > 0) do *op++ = *ip++; while (--t > 0);
                    }
                    else
                        do *op++ = *ip++; while (--t > 0);
                }
            first_literal_run:
                t = *ip++;
                if (t >= 16)
                    goto match;
                m_pos = op - (1 + 0x0800);
                m_pos -= t >> 2;
                m_pos -= *ip++ << 2;

                *op++ = *m_pos++; *op++ = *m_pos++; *op++ = *m_pos;
                gt_match_done = true;

            match:
                do
                {
                    if (gt_match_done)
                    {
                        gt_match_done = false;
                        goto match_done;
                        ;
                    }
                    if (t >= 64)
                    {
                        m_pos = op - 1;
                        m_pos -= (t >> 2) & 7;
                        m_pos -= *ip++ << 3;
                        t = (t >> 5) - 1;

                        copy_match(ref op, ref m_pos, ref t);
                        goto match_done;
                    }
                    else if (t >= 32)
                    {
                        t &= 31;
                        if (t == 0)
                        {
                            while (*ip == 0)
                            {
                                t += 255;
                                ip++;
                            }
                            t += (uint)(31 + *ip++);
                        }
                        m_pos = op - 1;
                        m_pos -= (*(ushort*)(void*)(ip)) >> 2;
                        ip += 2;
                    }
                    else if (t >= 16)
                    {
                        m_pos = op;
                        m_pos -= (t & 8) << 11;
                        t &= 7;
                        if (t == 0)
                        {
                            while (*ip == 0)
                            {
                                t += 255;
                                ip++;
                            }
                            t += (uint)(7 + *ip++);
                        }
                        m_pos -= (*(ushort*)ip) >> 2;
                        ip += 2;
                        if (m_pos == op)
                            goto eof_found;
                        m_pos -= 0x4000;
                    }
                    else
                    {
                        m_pos = op - 1;
                        m_pos -= t >> 2;
                        m_pos -= *ip++ << 2;
                        *op++ = *m_pos++; *op++ = *m_pos;
                        goto match_done;
                    }

                    if (t >= 2 * 4 - (3 - 1) && (op - m_pos) >= 4)
                    {
                        *(uint*)op = *(uint*)m_pos;
                        op += 4; m_pos += 4; t -= 4 - (3 - 1);
                        do
                        {
                            *(uint*)op = *(uint*)m_pos;
                            op += 4; m_pos += 4; t -= 4;
                        } while (t >= 4);
                        if (t > 0) do *op++ = *m_pos++; while (--t > 0);
                    }
                    else
                    {
                        // copy_match:
                        *op++ = *m_pos++; *op++ = *m_pos++;
                        do *op++ = *m_pos++; while (--t > 0);
                    }
                match_done:
                    t = (uint)(ip[-2] & 3);
                    if (t == 0)
                        break;
                    // match_next:
                    *op++ = *ip++;
                    if (t > 1) { *op++ = *ip++; if (t > 2) { *op++ = *ip++; } }
                    t = *ip++;
                } while (true);
            }
        eof_found:

            out_len = ((uint)((op) - (@out)));
            return (ip == ip_end ? 0 :
                   (ip < ip_end ? (-8) : (-4)));
        }

        private static unsafe void match_next(ref byte* op, ref byte* ip, ref uint t)
        {
            do *op++ = *ip++; while (--t > 0);
            t = *ip++;
        }

        private static unsafe void copy_match(ref byte* op, ref byte* m_pos, ref uint t)
        {
            *op++ = *m_pos++; *op++ = *m_pos++;
            do *op++ = *m_pos++; while (--t > 0);
        }



        public static unsafe byte[] Decompress(byte[] @in, byte[] @out)
        {
            uint out_len = 0;
            fixed (byte* @pIn = @in, wrkmem = new byte[IntPtr.Size * 16384], pOut = @out)
            {
                lzo1x_decompress(pIn, (uint)@in.Length, @pOut, ref @out_len, wrkmem);
            }
            return @out;
        }

        public static unsafe void Decompress(byte* r, uint size_in, byte* w, ref uint size_out)
        {
            fixed (byte* wrkmem = new byte[IntPtr.Size * 16384])
            {
                lzo1x_decompress(r, size_in, w, ref size_out, wrkmem);
            }
        }

        public static unsafe byte[] Compress(byte[] input)
        {
            byte[] @out = new byte[input.Length + (input.Length / 16) + 64 + 3];
            uint out_len = 0;
            fixed (byte* @pIn = input, wrkmem = new byte[IntPtr.Size * 16384], pOut = @out)
            {
                lzo1x_1_compress(pIn, (uint)input.Length, @pOut, ref @out_len, wrkmem);
            }
            Array.Resize(ref @out, (int)out_len);
            return @out;
        }

        public static unsafe void Compress(byte* r, uint size_in, byte* w, ref uint size_out)
        {
            fixed (byte* wrkmem = new byte[IntPtr.Size * 16384])
            {
                lzo1x_1_compress(r, size_in, w, ref size_out, wrkmem);
            }
        }
    }
    public class MapObject
    {
        public IsoTile Tile;
    }
    public class NamedMapObject : MapObject
    {
        public string Name { get; set; }
    }
    public class NumberedMapObject : MapObject
    {
        public virtual int Number { get; set; }
    }


    // all the stuff found on maps
    public class IsoTile : NumberedMapObject
    {
        public ushort Dx;//ushort Unsigned 16-bit integer 2-bytes
        public ushort Dy;
        public ushort Rx;
        public ushort Ry;
        public byte Z;//1 bytes
        public short TileNum;//16-bit
        public byte SubTile;//1 bytes

        public IsoTile(ushort p1, ushort p2, ushort rx, ushort ry, byte z, short tilenum, byte subtile)
        {
            Dx = p1;
            Dy = p2;
            Rx = rx;
            Ry = ry;
            Z = z;
            TileNum = tilenum;
            SubTile = subtile;
        }

        public List<byte> ToMapPack5Entry()
        {
            var ret = new List<byte>();
            ret.AddRange(BitConverter.GetBytes(Rx));//2 bytes
            ret.AddRange(BitConverter.GetBytes(Ry));//2 bytes
            ret.AddRange(BitConverter.GetBytes(TileNum));//2 bytes
            ret.Add(0); ret.Add(0);//1+1 bytes
            ret.Add(SubTile);//1 bytes
            ret.Add(Z);//1 bytes
            ret.Add(0);//1 bytes
            return ret;
        }


    }
    public class Format5
    {
        public static unsafe uint DecodeInto(byte[] src, byte[] dest, int format = 5)
        {
            fixed (byte* pr = src, pw = dest)
            {
                byte* r = pr, w = pw;
                byte* w_end = w + dest.Length;

                while (w < w_end)
                {
                    ushort size_in = *(ushort*)r;
                    r += 2;
                    uint size_out = *(ushort*)r;
                    r += 2;

                    if (size_in == 0 || size_out == 0)
                        break;

                    if (format == 80)
                        Format80.DecodeInto(r, w);
                    else
                        MiniLZO.Decompress(r, size_in, w, ref size_out);//默认是miniLZO
                    r += size_in;
                    w += size_out;
                }
                return (uint)(w - pw);//返回一个正值
            }
        }

        public static byte[] EncodeSection(byte[] s)
        {
            return MiniLZO.Compress(s);
        }

        public static byte[] Encode(byte[] source, int format)
        {
            var dest = new byte[source.Length * 2];
            var src = new MemoryFile(source);

            int w = 0;
            while (!src.Eof)
            {
                var cb_in = (short)Math.Min(src.Remaining, 8192);
                var chunk_in = src.Read(cb_in);
                var chunk_out = format == 80 ? Format80.Encode(chunk_in) : EncodeSection(chunk_in);
                uint cb_out = (ushort)chunk_out.Length;

                Array.Copy(BitConverter.GetBytes(cb_out), 0, dest, w, 2);
                w += 2;
                Array.Copy(BitConverter.GetBytes(cb_in), 0, dest, w, 2);
                w += 2;
                Array.Copy(chunk_out, 0, dest, w, chunk_out.Length);
                w += chunk_out.Length;
            }
            Array.Resize(ref dest, w);
            return dest;
        }
    }
    class Program
    {
        static void Main(string[] args)
        {
            //就改这里！
            string mapSection = System.IO.File.ReadAllText(@"C:\Users\16000\Desktop\IsoMapPack5.section");
            string Size_of_Map = System.IO.File.ReadAllText(@"C:\Users\16000\Desktop\mfs.size");
            string[] sArray = Size_of_Map.Split(",");
            int Width = Int32.Parse(sArray[2]);
            int Height = Int32.Parse(sArray[3]);
            Console.WriteLine("Width={0},Height={0}", Width, Height);
            int cells = (Width * 2 - 1) * Height;
            IsoTile[,] Tiles = new IsoTile[Width * 2 - 1, Height];//这里值得注意
            byte[] lzoData = Convert.FromBase64String(mapSection);

            //Console.WriteLine(cells);
            int lzoPackSize = cells * 11 + 4;
            var isoMapPack = new byte[lzoPackSize];
            uint totalDecompressSize = Format5.DecodeInto(lzoData, isoMapPack);//TODO 源，目标 输入应该是解码后长度，isoMapPack被赋值解码值了
                                                                               //uint	0 to 4,294,967,295	Unsigned 32-bit integer	System.UInt32
            var mf = new MemoryFile(isoMapPack);

            //Console.WriteLine(BitConverter.ToString(lzoData));
            int numtiles = 0;
            int count = 0;
            //List<List<IsoTile>> TilesList = new List<List<IsoTile>>(Width * 2 - 1);
            List<IsoTile> Tile_input_list = new List<IsoTile>();
            //Console.WriteLine(TilesList.Capacity);
            for (int i = 0; i < cells; i++)
            {
                //TODO 这些值是什么。
                ushort rx = mf.ReadUInt16();//ushort	0 to 65,535	Unsigned 16-bit integer	System.UInt16
                //Console.WriteLine($"rx=<rx>");
                ushort ry = mf.ReadUInt16();
                //Console.WriteLine("rx={0},ry={0}",rx,ry);
                short tilenum = mf.ReadInt16();//short	-32,768 to 32,767	Signed 16-bit integer	System.Int16
                //Console.WriteLine(tilenum);
                short zero1 = mf.ReadInt16();//Reads a 2-byte signed integer from the current stream and advances the current position of the stream by two bytes.
                byte subtile = mf.ReadByte();//Reads a byte from the stream and advances the position within the stream by one byte, or returns -1 if at the end of the stream.
                byte z = mf.ReadByte();
                byte zero2 = mf.ReadByte();
                //这是我用来调试的
                //if (tilenum==49){
                //  Console.WriteLine("rx={0},ry={1},tilenum={2},subtile={3},z={4}", rx, ry, tilenum, subtile, z); }
                //一次循环读11 bytes
                count++;
                int dx = rx - ry + Width - 1;

                int dy = rx + ry - Width - 1;
                //Console.WriteLine("{1}", rx, ry, tilenum, subtile, z, dx, dy,count);
                //上面是一个线性变换 旋转45度、拉长、平移
                numtiles++;//在最后日志用了一下
                //Console.WriteLine("Hello World 2");
                if (dx >= 0 && dx < 2 * Width &&
                    dy >= 0 && dy < 2 * Height)
                {
                    var tile = new IsoTile((ushort)dx, (ushort)dy, rx, ry, z, tilenum, subtile);//IsoTile定义是NumberedMapObject

                    Tiles[(ushort)dx, (ushort)dy / 2] = tile;//给瓷砖赋值
                    Tile_input_list.Add(tile);
                    // Console.WriteLine("{3}", dx, dy, rx,ry);
                    //Console.WriteLine("{1}",dx,dy/2,count);
                    //Console.WriteLine(tile.TileNum);
                    //Console.WriteLine("Hello World 1");
                }
            }
            //用来检查有没有空着的
            for (ushort y = 0; y < Height; y++)
            {
                for (ushort x = 0; x < Width * 2 - 1; x++)
                {
                    var isoTile = Tiles[x, y];//从这儿来看，isoTile指的是一块瓷砖，Tile是一个二维数组，存着所有瓷砖
                                              //isoTile的定义在TileLayer.cs里
                    if (isoTile == null)
                    {
                        //Console.WriteLine("null x={0},y={1}", x,y);
                        // fix null tiles to blank
                        ushort dx = (ushort)(x);
                        ushort dy = (ushort)(y * 2 + x % 2);
                        ushort rx = (ushort)((dx + dy) / 2 + 1);
                        ushort ry = (ushort)(dy - rx + Width + 1);
                        Tiles[x, y] = new IsoTile(dx, dy, rx, ry, 0, 0, 0);//TODO IsoTile有七个参数，定义在112行
                    }
                }

            }
            //IsoTile[] Tilelist = new IsoTile[(Width * 2 - 1)*Height];
            //var TilesList = new List<IsoTile>();
            //Console.WriteLine(TilesList);


            /*
            int count2 = 0;
            foreach (var tile in Tiles)
            {
                //Console.WriteLine("{2}", tile.Rx, tile.Ry,count2);
                count2++;
                Console.WriteLine("{0}", count2);
            }
            */
            //Console.WriteLine("Hello World!");
            //Console.WriteLine(Tiles);
            //以下是生成编码部分
            ///*
            long di = 0;
            var isoMapPack2 = new byte[lzoPackSize];
            foreach (var tile in Tile_input_list)
            {//if (tile != null) { //这里的判断是我加的TODO 为什么会有NULL
             //但是这样会导致生成的isoMapPack5和原来的不是一种了
             //Console.WriteLine("{1}", tile.Rx, tile.Ry);
                var bs = tile.ToMapPack5Entry().ToArray();//ToMapPack5Entry的定义在MapObjects.cs
                                                          //ToArray将ArrayList转换为Array：
                Array.Copy(bs, 0, isoMapPack2, di, 11);//把bs复制给isoMapPack,从di索引开始复制11个字节
                di += 11;//一次循环复制11个字节
                         // }
            }

            var compressed = Format5.Encode(isoMapPack2, 5);

            string compressed64 = Convert.ToBase64String(compressed);
            //Console.WriteLine(compressed64);
            int j = 1;
            int idx = 0;
            var isoMapPack5 = new IniFile.IniSection();//问题可能出在这里
            isoMapPack5.Clear();
            while (idx < compressed64.Length)
            {
                int adv = Math.Min(74, compressed64.Length - idx);//74是什么
                isoMapPack5.SetValue(j++.ToString(),
                                     compressed64.Substring(idx, adv));//start length
                idx += adv;//idx=adv+1
            }
            //Console.WriteLine(isoMapPack5);
            System.IO.File.WriteAllText(@"C:\Users\16000\Desktop\IsoMapPack5.section", Convert.ToString(isoMapPack5));
            //*/
        }
    }
}

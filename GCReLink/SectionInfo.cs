namespace GCReLink
{
    public sealed class SectionInfo
    {
        public int Id;
        public string Name;
        public int Size;

        public SectionInfo(int id, string name, int size)
        {
            Id = id;
            Name = name;
            Size = size;
        }
    }
}

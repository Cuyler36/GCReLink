namespace GCReLink.Analyzers.Data
{
    // Only supporting GPR registers at the moment
    public enum Register
    {
        R0,  R1,  R2,  R3,  R4,  R5,  R6,  R7, 
        R8,  R9,  R10, R11, R12, R13, R14, R15,
        R16, R17, R18, R19, R20, R21, R22, R23,
        R24, R25, R26, R27, R28, R29, R30, R31
    }

    public enum DataType
    {
        DATA, POINTER, BLOB_POINTER, STRUCT_POINTER
    }

    public sealed class RegisterContext
    {
        public readonly Register Register;
        public uint Contents;
        public DataType ContentsType;

        public RegisterContext(in Register register) => Register = register;

        public void SetContents(uint val) => Contents = val;
        public uint GetContents() => Contents;
        public void SetContentsType(DataType type) => ContentsType = type;
        public DataType GetContentsType() => ContentsType;
        public bool IsPointer => ContentsType == DataType.POINTER;
    }
}

using System.Data;
using System.Reflection.Metadata.Ecma335;

namespace SmugglerLib.DB;

public interface IDBConnector
{
    bool SetConnectionString(string connectionString, string databaseName);

    bool CallProcedure(string ProcedureName, params PGSQLParam[] sqlparameter);
}

public class PGSQLParam
{
    public bool InOut { get; set; } = true;
    public string ParamName { get; set; }

    public PGParamType pType { get; set; }

    public DbType DBType => GetDbType(pType);

    private DbType GetDbType(PGParamType pType)
    {
        return pType switch
        {
            PGParamType.IntType => DbType.Int32,
            PGParamType.StringType => DbType.String,
            PGParamType.BoolType => DbType.Boolean,
            PGParamType.DateType => DbType.DateTime,
            _ => DbType.String,
        };
    }
}

public enum PGParamType
{
    IntType,
    StringType,
    BoolType,
    DateType,
}
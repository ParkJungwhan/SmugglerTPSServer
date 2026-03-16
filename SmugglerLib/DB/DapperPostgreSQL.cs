using System.Data;
using System.Data.Common;
using Dapper;
using Npgsql;
using SmugglerLib.Commons;

namespace SmugglerLib.DB;

public class DapperPostgreSQL : IDBConnector
{
    private DbConnection Connection;
    private const string ExceptionString = "[DBException] ";
    private readonly string DataBasename = "";

    public bool SetConnectionString(string connectionString, string databaseName)
    {
        try
        {
            Connection = new NpgsqlConnection(connectionString);
            if (databaseName.ToLower() != Connection.Database.ToLower())
                Log.PrintError(ExceptionString + "Fail to compare DatabaseName");
        }
        catch (Exception ex)
        {
            Log.PrintError(ExceptionString + ex.Message);
            Log.PrintError(ex.Source);

            return false;
        }

        return true;
    }

    public bool CallProcedure(string ProcedureName, params PGSQLParam[] sqlparameter)
    {
        try
        {
            var parameters = new DynamicParameters();

            for (int i = 0; i < sqlparameter.Length; i++)
            {
                parameters.Add(
                    sqlparameter[i].ParamName,
                    null,
                    sqlparameter[i].DBType,
                    sqlparameter[i].InOut ? ParameterDirection.Input : ParameterDirection.Output);
            }

            Connection.Execute(
                ProcedureName,
                parameters,
                commandType: CommandType.StoredProcedure
            );

            return parameters.Get<int>("result_code");
        }
        catch (Exception ex)
        {
            Log.PrintError(ExceptionString + ex.Message);
            Log.PrintError(ex.Source);
            return false;
        }
        return true;
    }

    //public IEnumerable<T> GetQuery<T>(string Catalog, string Query)
    //{
    //    try
    //    {
    //        if (Connection.State != System.Data.ConnectionState.Open) Connection.Open();

    //        using (var command = Connection.CreateCommand())
    //        {
    //            command.CommandText = $"SET search_path TO {Catalog}; {Query}";
    //            using (var reader = command.ExecuteReader())
    //            {
    //                var results = new List<Dictionary<string, object>>();
    //                while (reader.Read())
    //                {
    //                    var row = new Dictionary<string, object>();
    //                    for (int i = 0; i < reader.FieldCount; i++)
    //                    {
    //                        row[reader.GetName(i)] = reader.GetValue(i);
    //                    }
    //                    results.Add(row);
    //                }
    //                return results.Cast<T>();
    //            }
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Log.PrintError(ExceptionString + ex.Message);
    //        Log.PrintError(ex.Source);
    //        return Enumerable.Empty<T>();
    //    }
    //}
}
using Protocol;
using SmugglerLib.DB;

namespace SmugglerServer.DB;

internal class UserDBService
{
    private IDBConnector GameDBConnector;
    private IDBConnector LogDBConnector;

    internal bool AddOrUpdateUserData(CLAuthRequest authData)
    {
        if (string.IsNullOrEmpty(authData.DeviceKey)) return false;
        if (string.IsNullOrEmpty(authData.UserName)) return false;
        if (authData.AppearanceId <= 0) return false;

        List<PGSQLParam> sqlParams = new List<PGSQLParam>(4) {
        new() { ParamName = "p_pid", pType = PGParamType.IntType }
        ,new() { ParamName = "p_dkey", pType = PGParamType.StringType }
        ,new() { ParamName = "p_username", pType = PGParamType.StringType }
        ,new() { ParamName = "p_uptime", pType = PGParamType.DateType }
        ,new() { ParamName = "result_code", pType = PGParamType.IntType, InOut = false }    //output
        };

        GameDBConnector.CallProcedure("sp_setuser", sqlParams);

        return true;
    }
}
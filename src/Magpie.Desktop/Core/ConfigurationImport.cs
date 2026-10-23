namespace Magpie.Desktop.Core;

internal static class ConfigurationImport
{
    // Called synchronously on the UI thread after review. The current process must exit immediately
    // after success; it keeps its old in-memory settings so a failed restart can resume unchanged.
    internal static string? Install(RuntimeSettings draft,string path,Action restart)
    {
        draft.Validate();path=Path.GetFullPath(path);
        string? backup=null;bool existed=File.Exists(path);
        if(existed)
        {
            backup=path+".before-import-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",System.Globalization.CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N")+".json";
            File.Copy(path,backup,false);
        }
        byte[] installed=System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(draft,SettingsJson.Default.RuntimeSettings));
        draft.WriteFile(path);
        try { restart();return backup; }
        catch(Exception failure)
        {
            try
            {
                if(!File.ReadAllBytes(path).AsSpan().SequenceEqual(installed))throw new IOException("The configuration changed after import; automatic rollback would overwrite another writer. Original backup: "+backup);
                if(existed)
                {
                    string temporary=path+"."+Guid.NewGuid().ToString("N")+".rollback";
                    try { File.Copy(backup!,temporary,false);File.Move(temporary,path,true); }
                    finally { if(File.Exists(temporary))File.Delete(temporary); }
                }
                else File.Delete(path);
            }
            catch(Exception rollback)
            { throw new IOException("Restart failed and the original configuration could not be restored. "+rollback.Message,new AggregateException(failure,rollback)); }
            throw new IOException("Restart failed; the original configuration was restored. "+failure.Message,failure);
        }
    }
}

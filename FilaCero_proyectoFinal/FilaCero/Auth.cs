using System.Security.Cryptography;
using MySqlConnector;

namespace FilaCero;

public static class Auth
{
    public static async Task AsegurarAsync()
    {
        await using var con = Db.Conectar(); await con.OpenAsync();
        await using (var cmd = new MySqlCommand(@"CREATE TABLE IF NOT EXISTS cuentas (
            id INT AUTO_INCREMENT PRIMARY KEY,
            login VARCHAR(50) NOT NULL UNIQUE,
            clave_hash VARCHAR(200) NOT NULL) ENGINE=InnoDB", con))
            await cmd.ExecuteNonQueryAsync();

        await using var cnt = new MySqlCommand("SELECT COUNT(*) FROM cuentas", con);
        if (Convert.ToInt64(await cnt.ExecuteScalarAsync()) == 0)
        {
            await using var ins = new MySqlCommand("INSERT INTO cuentas (login, clave_hash) VALUES (@l,@h)", con);
            ins.Parameters.AddWithValue("@l", "admin");
            ins.Parameters.AddWithValue("@h", Hashear("admin123"));
            await ins.ExecuteNonQueryAsync();
        }
    }

    public static async Task<bool> ValidarAsync(string login, string clave)
    {
        await using var con = Db.Conectar(); await con.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT clave_hash FROM cuentas WHERE login=@l", con);
        cmd.Parameters.AddWithValue("@l", login);
        var guardado = await cmd.ExecuteScalarAsync() as string;
        return guardado != null && Verificar(clave, guardado);
    }

    private static string Hashear(string clave)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(clave, sal, 100_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(sal) + ":" + Convert.ToBase64String(hash);
    }

    private static bool Verificar(string clave, string guardado)
    {
        var p = guardado.Split(':');
        if (p.Length != 2) return false;
        var hash = Rfc2898DeriveBytes.Pbkdf2(clave, Convert.FromBase64String(p[0]), 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(p[1]));
    }
}

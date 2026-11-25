using MySqlConnector;

namespace FilaCero;

public static class Db
{
    public static string Servidor { get; } = Environment.GetEnvironmentVariable("MARIADB_SERVER") ?? "localhost";
    public static string Usuario { get; } = Environment.GetEnvironmentVariable("MARIADB_USER") ?? "root";
    public static string Clave { get; } = Environment.GetEnvironmentVariable("MARIADB_PASSWORD") ?? "";
    public static string BaseDatos { get; } = "filacero";
    public static uint Puerto => uint.TryParse(Environment.GetEnvironmentVariable("MARIADB_PORT"), out var p) ? p : 3306;

    public static string CadenaSinBase => new MySqlConnectionStringBuilder
    {
        Server = Servidor,
        Port = Puerto,
        UserID = Usuario,
        Password = Clave,
        SslMode = MySqlSslMode.None,
        AllowPublicKeyRetrieval = true,
        ConnectionTimeout = 10
    }.ConnectionString;

    public static string Cadena => new MySqlConnectionStringBuilder(CadenaSinBase) { Database = BaseDatos }.ConnectionString;
    public static MySqlConnection Conectar() => new(Cadena);

    // Cada paso va por separado: si algo falla, el mensaje dice EXACTAMENTE cuál.
    private static async Task EjecutarAsync(MySqlConnection con, string paso, string sql)
    {
        try
        {
            await using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"Falló el paso: {paso}\n\n{ex.Message}", ex);
        }
    }

    private static async Task<long> ContarAsync(MySqlConnection con, string tabla)
    {
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM `{tabla}`";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public static async Task<string> InicializarAsync()
    {
        // 1) Conectar al servidor (sin elegir base todavía)
        await using var con = new MySqlConnection(CadenaSinBase);
        try
        {
            await con.OpenAsync();
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.AccessDenied)
        {
            throw new Exception($"Usuario o contraseña incorrectos para '{Usuario}'. Vuelve a abrir el .bat y escribe bien la contraseña de MariaDB.", ex);
        }
        catch (Exception ex)
        {
            throw new Exception($"No se pudo conectar a MariaDB en {Servidor}:{Puerto}. ¿El servicio está iniciado?\n\n{ex.Message}", ex);
        }

        // 2) Crear base y seleccionarla
        await EjecutarAsync(con, "crear base de datos",
            $"CREATE DATABASE IF NOT EXISTS `{BaseDatos}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
        await EjecutarAsync(con, "seleccionar base de datos", $"USE `{BaseDatos}`");

        // 3) Tablas (en orden, por las llaves foráneas)
        await EjecutarAsync(con, "tabla establecimientos", @"
CREATE TABLE IF NOT EXISTS establecimientos (
    id INT AUTO_INCREMENT PRIMARY KEY,
    nombre VARCHAR(120) NOT NULL,
    tipo VARCHAR(80) NOT NULL
) ENGINE=InnoDB");

        await EjecutarAsync(con, "tabla servicios", @"
CREATE TABLE IF NOT EXISTS servicios (
    id INT AUTO_INCREMENT PRIMARY KEY,
    establecimiento_id INT NOT NULL,
    nombre VARCHAR(120) NOT NULL,
    minutos_promedio INT NOT NULL DEFAULT 10,
    FOREIGN KEY (establecimiento_id) REFERENCES establecimientos(id)
) ENGINE=InnoDB");

        await EjecutarAsync(con, "tabla usuarios", @"
CREATE TABLE IF NOT EXISTS usuarios (
    id INT AUTO_INCREMENT PRIMARY KEY,
    nombre VARCHAR(120) NOT NULL,
    telefono VARCHAR(30) NULL
) ENGINE=InnoDB");

        await EjecutarAsync(con, "tabla turnos", @"
CREATE TABLE IF NOT EXISTS turnos (
    id INT AUTO_INCREMENT PRIMARY KEY,
    codigo VARCHAR(12) NOT NULL,
    usuario_id INT NOT NULL,
    servicio_id INT NOT NULL,
    fecha DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    estado ENUM('Pendiente','Atendiendo','Atendido','Cancelado') NOT NULL DEFAULT 'Pendiente',
    FOREIGN KEY (usuario_id) REFERENCES usuarios(id),
    FOREIGN KEY (servicio_id) REFERENCES servicios(id)
) ENGINE=InnoDB");

        await EjecutarAsync(con, "índice de turnos",
            "CREATE INDEX IF NOT EXISTS idx_turnos_servicio_estado_fecha ON turnos (servicio_id, estado, fecha)");

        // 4) Datos iniciales (la lógica del "si está vacío" la hace C#, no un SELECT raro)
        if (await ContarAsync(con, "establecimientos") == 0)
        {
            await EjecutarAsync(con, "datos iniciales: establecimientos", @"
INSERT INTO establecimientos (nombre, tipo) VALUES
 ('Centro de Atención Municipal', 'Oficina pública'),
 ('Clínica FilaCero', 'Salud')");
        }

        if (await ContarAsync(con, "servicios") == 0)
        {
            await EjecutarAsync(con, "datos iniciales: servicios", @"
INSERT INTO servicios (establecimiento_id, nombre, minutos_promedio)
SELECT id, 'Atención general', 10 FROM establecimientos");
            await EjecutarAsync(con, "datos iniciales: servicio de orientación", @"
INSERT INTO servicios (establecimiento_id, nombre, minutos_promedio)
SELECT id, 'Orientación y consultas', 15 FROM establecimientos
WHERE nombre = 'Centro de Atención Municipal'");
        }

        // 5) Comprobar y devolver DÓNDE quedó la base (para verla en HeidiSQL/consola)
        await using var info = con.CreateCommand();
        info.CommandText = "SELECT VERSION(), @@port, @@hostname, (SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = 'filacero')";
        await using var r = await info.ExecuteReaderAsync();
        await r.ReadAsync();
        return $"Base '{BaseDatos}' lista (existe: {(Convert.ToInt32(r.GetValue(3)) == 1 ? "SÍ" : "NO")}).\n\n" +
               $"Servidor: {r.GetValue(2)}\nPuerto: {r.GetValue(1)}\nVersión: {r.GetValue(0)}\nUsuario: {Usuario}\n\n" +
               "En HeidiSQL conéctate a ESE puerto y presiona F5 para refrescar.";
    }
}

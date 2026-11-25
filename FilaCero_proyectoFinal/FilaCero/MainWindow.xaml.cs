using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MySqlConnector;

namespace FilaCero;

public partial class MainWindow : Window
{
    private int turnoSeleccionado;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                await CargarEstablecimientosAsync();
                await CargarTurnosAsync();
            }
            catch (Exception ex) { Error(ex); }
        };
    }

    private async Task CargarEstablecimientosAsync()
    {
        await using var db = Db.Conectar(); await db.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT id, nombre FROM establecimientos ORDER BY nombre", db);
        await using var reader = await cmd.ExecuteReaderAsync();
        var tabla = new DataTable(); tabla.Load(reader);
        cmbEstablecimiento.ItemsSource = tabla.DefaultView;
        cmbEstablecimiento.DisplayMemberPath = "nombre";
        cmbEstablecimiento.SelectedValuePath = "id";
        if (cmbEstablecimiento.Items.Count > 0) cmbEstablecimiento.SelectedIndex = 0;
    }

    private async void cmbEstablecimiento_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbEstablecimiento.SelectedValue is null) return;
        await CargarServiciosAsync(Convert.ToInt32(cmbEstablecimiento.SelectedValue));
    }

    private async Task CargarServiciosAsync(int establecimientoId)
    {
        await using var db = Db.Conectar(); await db.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT id, nombre, minutos_promedio FROM servicios WHERE establecimiento_id=@id ORDER BY nombre", db);
        cmd.Parameters.AddWithValue("@id", establecimientoId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var tabla = new DataTable(); tabla.Load(reader);
        cmbServicio.ItemsSource = tabla.DefaultView;
        cmbServicio.DisplayMemberPath = "nombre";
        cmbServicio.SelectedValuePath = "id";
        if (cmbServicio.Items.Count > 0) cmbServicio.SelectedIndex = 0;
    }

    private async Task CargarTurnosAsync()
    {
        await using var db = Db.Conectar(); await db.OpenAsync();
        const string sql = @"SELECT t.id, t.codigo, u.nombre usuario, e.nombre establecimiento,
 s.nombre servicio, (SELECT COUNT(*) FROM turnos t2 WHERE t2.servicio_id=t.servicio_id
 AND t2.estado='Pendiente' AND (t2.fecha<t.fecha OR (t2.fecha=t.fecha AND t2.id<t.id))) personas_delante,
 ((SELECT COUNT(*) FROM turnos t2 WHERE t2.servicio_id=t.servicio_id AND t2.estado='Pendiente'
 AND (t2.fecha<t.fecha OR (t2.fecha=t.fecha AND t2.id<t.id))) * s.minutos_promedio) espera_minutos, t.estado
 FROM turnos t JOIN usuarios u ON u.id=t.usuario_id JOIN servicios s ON s.id=t.servicio_id
 JOIN establecimientos e ON e.id=s.establecimiento_id WHERE t.estado IN ('Pendiente','Atendiendo') ORDER BY t.fecha, t.id";
        await using var cmd = new MySqlCommand(sql, db);
        await using var reader = await cmd.ExecuteReaderAsync();
        var tabla = new DataTable(); tabla.Load(reader); dgTurnos.ItemsSource = tabla.DefaultView;
        lblPendientes.Text = tabla.Select("estado = 'Pendiente'").Length.ToString();
        lblAtendiendo.Text = tabla.Select("estado = 'Atendiendo'").Length.ToString();
        btnSiguiente.IsEnabled = tabla.Select("estado = 'Pendiente'").Length > 0;
        AplicarFiltro();
        await ActualizarInfoServicioAsync();
    }

    private async void btnTomarTurno_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtNombre.Text) || cmbServicio.SelectedValue is null)
        { MessageBox.Show("Escribe el nombre y selecciona un servicio."); return; }
        try
        {
            await using var db = Db.Conectar(); await db.OpenAsync();
            await using var trans = await db.BeginTransactionAsync();
            var usuario = new MySqlCommand("INSERT INTO usuarios (nombre, telefono) VALUES (@n,@t); SELECT LAST_INSERT_ID();", db, trans);
            usuario.Parameters.AddWithValue("@n", txtNombre.Text.Trim()); usuario.Parameters.AddWithValue("@t", txtTelefono.Text.Trim());
            int usuarioId = Convert.ToInt32(await usuario.ExecuteScalarAsync());
            int servicioId = Convert.ToInt32(cmbServicio.SelectedValue);
            var codigo = new MySqlCommand("SELECT CONCAT('A', LPAD(COUNT(*)+1,3,'0')) FROM turnos WHERE servicio_id=@s AND DATE(fecha)=CURDATE()", db, trans);
            codigo.Parameters.AddWithValue("@s", servicioId); string numero = Convert.ToString(await codigo.ExecuteScalarAsync()) ?? "A001";
            var turno = new MySqlCommand("INSERT INTO turnos (codigo, usuario_id, servicio_id) VALUES (@c,@u,@s)", db, trans);
            turno.Parameters.AddWithValue("@c", numero); turno.Parameters.AddWithValue("@u", usuarioId); turno.Parameters.AddWithValue("@s", servicioId); await turno.ExecuteNonQueryAsync();
            await trans.CommitAsync();
            MessageBox.Show("Turno asignado: " + numero); Limpiar(); await CargarTurnosAsync();
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void btnSiguiente_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await using var db = Db.Conectar(); await db.OpenAsync();
            await using var cmd = new MySqlCommand("UPDATE turnos SET estado='Atendiendo' WHERE id=(SELECT id FROM (SELECT id FROM turnos WHERE estado='Pendiente' ORDER BY fecha,id LIMIT 1) x)", db);
            await cmd.ExecuteNonQueryAsync(); await CargarTurnosAsync();
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void btnAtender_Click(object sender, RoutedEventArgs e) => await CambiarEstadoAsync("Atendido");
    private async void btnCancelar_Click(object sender, RoutedEventArgs e)
    {
        if (turnoSeleccionado != 0 &&
            MessageBox.Show("¿Seguro que deseas cancelar este turno?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await CambiarEstadoAsync("Cancelado");
    }

    private async Task CambiarEstadoAsync(string estado)
    {
        if (turnoSeleccionado == 0) { MessageBox.Show("Selecciona un turno."); return; }
        try
        {
            await using var db = Db.Conectar(); await db.OpenAsync();
            await using var cmd = new MySqlCommand("UPDATE turnos SET estado=@e WHERE id=@id", db);
            cmd.Parameters.AddWithValue("@e", estado); cmd.Parameters.AddWithValue("@id", turnoSeleccionado); await cmd.ExecuteNonQueryAsync();
            turnoSeleccionado = 0; await CargarTurnosAsync();
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void btnRecargar_Click(object sender, RoutedEventArgs e) { try { await CargarTurnosAsync(); } catch (Exception ex) { Error(ex); } }

    private void dgTurnos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (dgTurnos.SelectedItem is DataRowView fila) turnoSeleccionado = Convert.ToInt32(fila["id"]); }

    private void btnSalir_Click(object sender, RoutedEventArgs e)
    {
        var login = new LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();
        Close();
    }

    // ---- Información de espera del servicio seleccionado ----
    private async void cmbServicio_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try { await ActualizarInfoServicioAsync(); } catch (Exception ex) { Error(ex); }
    }

    private async Task ActualizarInfoServicioAsync()
    {
        if (cmbServicio.SelectedItem is not DataRowView fila) { lblInformacion.Text = "Seleccione un servicio"; return; }
        int servicioId = Convert.ToInt32(fila["id"]);
        int minutos = Convert.ToInt32(fila["minutos_promedio"]);
        await using var db = Db.Conectar(); await db.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM turnos WHERE servicio_id=@s AND estado='Pendiente'", db);
        cmd.Parameters.AddWithValue("@s", servicioId);
        int enEspera = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        lblInformacion.Text = $"Atención: {minutos} min c/u\nEn espera: {enEspera} · Espera aprox.: {enEspera * minutos} min";
    }

    // ---- Buscador ----
    private void txtBuscar_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void AplicarFiltro()
    {
        if (dgTurnos is null || txtBuscar is null || dgTurnos.ItemsSource is not DataView vista) return;
        var sb = new StringBuilder();
        foreach (char ch in txtBuscar.Text.Trim())
        {
            if (ch == '\'') sb.Append("''");
            else if ("[]%*".IndexOf(ch) >= 0) sb.Append('[').Append(ch).Append(']');
            else sb.Append(ch);
        }
        string t = sb.ToString();
        vista.RowFilter = t.Length == 0 ? "" :
            $"usuario LIKE '%{t}%' OR codigo LIKE '%{t}%' OR servicio LIKE '%{t}%' OR establecimiento LIKE '%{t}%'";
    }

    // ---- Exportar a CSV (se abre en Excel) ----
    private void btnExportar_Click(object sender, RoutedEventArgs e)
    {
        if (dgTurnos.ItemsSource is not DataView vista || vista.Count == 0)
        { MessageBox.Show("No hay turnos para exportar."); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Archivo CSV (*.csv)|*.csv",
            FileName = $"turnos_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var sb = new StringBuilder();
            string[] cols = { "codigo", "usuario", "establecimiento", "servicio", "personas_delante", "espera_minutos", "estado" };
            sb.AppendLine("Turno;Usuario;Establecimiento;Servicio;Personas delante;Espera (min);Estado");
            foreach (DataRowView fila in vista)
                sb.AppendLine(string.Join(";", cols.Select(k => "\"" + Convert.ToString(fila[k])?.Replace("\"", "\"\"") + "\"")));
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            MessageBox.Show("Exportado correctamente.", "FilaCero", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "No se pudo exportar", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Limpiar() { txtNombre.Clear(); txtTelefono.Clear(); txtNombre.Focus(); }
    private static void Error(Exception ex) => MessageBox.Show(ex.Message, "Error de MariaDB", MessageBoxButton.OK, MessageBoxImage.Error);
}

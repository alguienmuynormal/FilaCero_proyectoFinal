-- Opcional: solo si quieres crear la base a mano (HeidiSQL / consola de MariaDB).
-- La aplicación ya hace todo esto sola al abrirse.
CREATE DATABASE IF NOT EXISTS filacero CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE filacero;

CREATE TABLE IF NOT EXISTS establecimientos (
 id INT AUTO_INCREMENT PRIMARY KEY,
 nombre VARCHAR(120) NOT NULL,
 tipo VARCHAR(80) NOT NULL
);
CREATE TABLE IF NOT EXISTS servicios (
 id INT AUTO_INCREMENT PRIMARY KEY,
 establecimiento_id INT NOT NULL,
 nombre VARCHAR(120) NOT NULL,
 minutos_promedio INT NOT NULL DEFAULT 10,
 FOREIGN KEY (establecimiento_id) REFERENCES establecimientos(id)
);
CREATE TABLE IF NOT EXISTS usuarios (
 id INT AUTO_INCREMENT PRIMARY KEY,
 nombre VARCHAR(120) NOT NULL,
 telefono VARCHAR(30)
);
CREATE TABLE IF NOT EXISTS turnos (
 id INT AUTO_INCREMENT PRIMARY KEY,
 codigo VARCHAR(12) NOT NULL,
 usuario_id INT NOT NULL,
 servicio_id INT NOT NULL,
 fecha DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
 estado ENUM('Pendiente','Atendiendo','Atendido','Cancelado') NOT NULL DEFAULT 'Pendiente',
 FOREIGN KEY (usuario_id) REFERENCES usuarios(id),
 FOREIGN KEY (servicio_id) REFERENCES servicios(id)
);
CREATE INDEX IF NOT EXISTS idx_turnos_servicio_estado_fecha ON turnos (servicio_id, estado, fecha);

INSERT INTO establecimientos (nombre, tipo) VALUES
 ('Centro de Atención Municipal', 'Oficina pública'),
 ('Clínica FilaCero', 'Salud');
INSERT INTO servicios (establecimiento_id, nombre, minutos_promedio)
 SELECT id, 'Atención general', 10 FROM establecimientos;
INSERT INTO servicios (establecimiento_id, nombre, minutos_promedio)
 SELECT id, 'Orientación y consultas', 15 FROM establecimientos WHERE nombre = 'Centro de Atención Municipal';

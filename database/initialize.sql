-- Solo el inicializador utiliza sa. Las APIs tienen acceso a su propia base.
IF DB_ID('EduMonitor_Usuarios') IS NULL CREATE DATABASE EduMonitor_Usuarios;
IF DB_ID('EduMonitor_Materias') IS NULL CREATE DATABASE EduMonitor_Materias;
IF DB_ID('EduMonitor_Estudiantes') IS NULL CREATE DATABASE EduMonitor_Estudiantes;
GO
IF NOT EXISTS(SELECT 1 FROM sys.server_principals WHERE name='edumonitor_usuarios') CREATE LOGIN edumonitor_usuarios WITH PASSWORD=N'$(USUARIOS_PASSWORD)', CHECK_POLICY=ON;
IF NOT EXISTS(SELECT 1 FROM sys.server_principals WHERE name='edumonitor_materias') CREATE LOGIN edumonitor_materias WITH PASSWORD=N'$(MATERIAS_PASSWORD)', CHECK_POLICY=ON;
IF NOT EXISTS(SELECT 1 FROM sys.server_principals WHERE name='edumonitor_estudiantes') CREATE LOGIN edumonitor_estudiantes WITH PASSWORD=N'$(ESTUDIANTES_PASSWORD)', CHECK_POLICY=ON;
GO
USE EduMonitor_Usuarios;
IF USER_ID('edumonitor_usuarios') IS NULL CREATE USER edumonitor_usuarios FOR LOGIN edumonitor_usuarios;
ALTER ROLE db_owner ADD MEMBER edumonitor_usuarios;
GO
USE EduMonitor_Materias;
IF USER_ID('edumonitor_materias') IS NULL CREATE USER edumonitor_materias FOR LOGIN edumonitor_materias;
ALTER ROLE db_owner ADD MEMBER edumonitor_materias;
GO
USE EduMonitor_Estudiantes;
IF USER_ID('edumonitor_estudiantes') IS NULL CREATE USER edumonitor_estudiantes FOR LOGIN edumonitor_estudiantes;
ALTER ROLE db_owner ADD MEMBER edumonitor_estudiantes;
GO

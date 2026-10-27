-- Migración puntual, fuera de las APIs. La base anterior no se elimina ni modifica.
IF DB_ID('EduMonitorAI') IS NOT NULL
BEGIN
    IF EXISTS(SELECT 1 FROM EduMonitorAI.dbo.Courses) OR EXISTS(SELECT 1 FROM EduMonitorAI.dbo.Students)
       OR EXISTS(SELECT 1 FROM EduMonitorAI.dbo.Users WHERE Role <> 'Admin')
        THROW 51000, 'La base anterior contiene registros académicos. Conservada sin cambios: requiere migración supervisada de período y apellidos.', 1;
    UPDATE target SET target.PasswordHash=source.PasswordHash, target.Name=source.Name,
        target.TokenVersion=source.TokenVersion
    FROM EduMonitor_Usuarios.dbo.Users target
    INNER JOIN EduMonitorAI.dbo.Users source ON source.Email=target.Email
    WHERE source.Role='Admin' AND target.Role='Admin';
END;

# Despliegue en Railway con Supabase

El proyecto usa EF Core 10 y admite PostgreSQL mediante una migración propia
para `PostgreSqlApplicationDbContext`. Las migraciones SQL Server existentes no
se aplican a PostgreSQL.

## 1. Preparar Supabase

En el panel del proyecto, abre **Connect** y copia los datos de conexión de
PostgreSQL. Para Railway, usa la conexión **Session pooler** si el servicio no
puede alcanzar la conexión directa IPv6. No uses el modo Transaction pooler para
esta aplicación EF Core. Utiliza el host, puerto y nombre de usuario que muestra
Supabase; el usuario del pooler puede incluir el identificador del proyecto.

La cuenta de base de datos debe poder crear tablas e índices en el esquema
configurado (por defecto, `public`). Para el primer despliegue, usa un esquema
sin tablas previas de esta aplicación. Esto crea el esquema inicial, pero no
transfiere usuarios ni otros datos de la base local MySQL/MariaDB.

## 2. Desplegar en Railway

Conecta el repositorio a Railway y deja el directorio raíz del servicio en la
raíz del repositorio. Railway usará el `Dockerfile` incluido. El contenedor
instala los certificados CA y la biblioteca GSSAPI de Kerberos requeridos por
el runtime Linux y la conexión TLS de PostgreSQL, y escucha en la variable
`PORT` que Railway proporciona.

Configura estas variables en **Railway → Service → Variables**:

| Variable | Valor |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Database__Provider` | `PostgreSql` |
| `ConnectionStrings__DefaultConnection` | `Host=<host>;Port=<puerto>;Database=postgres;Username=<usuario>;Password=<contraseña>;SSL Mode=Require;Trust Server Certificate=true` |
| `BootstrapAdmin__Email` | Correo inicial para iniciar sesión |
| `BootstrapAdmin__Password` | Contraseña inicial segura (al menos 12 caracteres) |

Usa los valores exactos de **Connect** de Supabase en la cadena de conexión.
Guárdala como variable secreta de Railway; no la añadas a `appsettings.json`,
al repositorio ni a mensajes.

Al arrancar, la aplicación ejecuta las migraciones PostgreSQL y crea la cuenta
inicial si todavía no existe. Verifica el despliegue usando
`/Account/Login` como ruta de comprobación HTTP. Después del primer arranque
correcto, puedes quitar `BootstrapAdmin__Password` de Railway; la cuenta creada
permanece en Supabase.

El monitoreo usa datos simulados por defecto. Para conectar Zabbix, configura
`Monitoring__Source=Zabbix`, `Zabbix__ApiUrl` y `Zabbix__ApiToken` en Railway.
Las notificaciones por correo siguen desactivadas salvo que se configure
`Notifications__Enabled=true` y los valores de `EmailSettings`.

## Desarrollo local

`appsettings.Development.json` sigue seleccionando MySQL/MariaDB. Configura su
cadena local mediante User Secrets y no reemplaces esa conexión con la de
Supabase a menos que quieras probar localmente contra PostgreSQL.

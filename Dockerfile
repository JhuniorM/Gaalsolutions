FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Gaalsolutions.Monitoring/Gaalsolutions.Monitoring.csproj Gaalsolutions.Monitoring/
RUN dotnet restore Gaalsolutions.Monitoring/Gaalsolutions.Monitoring.csproj

COPY Gaalsolutions.Monitoring/ Gaalsolutions.Monitoring/
WORKDIR /src/Gaalsolutions.Monitoring
RUN dotnet publish Gaalsolutions.Monitoring.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates libgssapi-krb5-2 openssl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["sh", "-c", "exec dotnet Gaalsolutions.Monitoring.dll --urls http://0.0.0.0:${PORT:-8080}"]

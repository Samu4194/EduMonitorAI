FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY shared/ shared/
COPY services/evaluaciones/ services/evaluaciones/
RUN dotnet publish services/evaluaciones/EduMonitor.Evaluaciones.csproj -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
# Carpeta de los archivos de los temas (se monta un volumen): debe poder escribirla el usuario del servicio
RUN mkdir -p /data/archivos && chown $APP_UID /data/archivos
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet","EduMonitor.Evaluaciones.dll"]


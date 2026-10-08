FROM node:24-alpine AS frontend
WORKDIR /ui
COPY frontend/package*.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/ ./src/
RUN dotnet publish src/ShabbatBoard.Api -c Release -o /publish
COPY --from=frontend /ui/dist /publish/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /publish ./
RUN mkdir -p /app/data && chown -R app:app /app/data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Persistence__FilePath=/app/data/assignments.json
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "ShabbatBoard.Api.dll"]

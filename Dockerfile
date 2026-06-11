# ---- Build Stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# ---- Runtime Stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/data
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/LostAndFound.db"
ENV ASPNETCORE_URLS="http://0.0.0.0:8080"
EXPOSE 8080
VOLUME ["/app/data"]
ENTRYPOINT ["dotnet", "LostAndFound.dll"]

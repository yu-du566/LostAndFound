# ---- Build Stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

# ---- Runtime Stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/data
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
EXPOSE 5000
VOLUME ["/app/data"]
ENTRYPOINT ["dotnet", "LostAndFound.dll"]

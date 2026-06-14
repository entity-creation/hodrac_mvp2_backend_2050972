# Step 1: Use the official .NET SDK image to build the app (Assuming .NET 8.0, adjust if using 7.0 or 9.0)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies
COPY ["Hodrac_Backend_MVP2.csproj", "./"]
RUN dotnet restore "Hodrac_Backend_MVP2.csproj"

# Copy the remaining source code and build the app
COPY . .
RUN dotnet build "Hodrac_Backend_MVP2.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "Hodrac_Backend_MVP2.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Step 2: Use the lightweight runtime image to run the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Install cultures/globalization support (highly recommended for travel/localized platforms)
RUN apt-get update && apt-get install -y icu-data icu-devtools && rm -rf /var/lib/apt/lists/*
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Render dynamically assigns a port via the PORT env variable. 
# We default to 10000 if not set, but allow Render to override it.
ENV ASPNETCORE_URLS=http://+:${PORT:-10000}
EXPOSE 10000

# Using a shell form entrypoint ensures the ${PORT} variable evaluates correctly on Render
ENTRYPOINT ["sh", "-c", "dotnet Hodrac_Backend_MVP2.dll"]
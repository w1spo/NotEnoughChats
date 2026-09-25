# NEC

## NotEnoughChats

Simple command-line chat application built with C# and Firebase.

<p>
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square" alt="Platform">
  <img src="https://img.shields.io/badge/Language-C%23-239120?style=flat-square" alt="C#">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square" alt=".NET 8">
  <img src="https://img.shields.io/github/license/w1spo/notenoughchats?style=flat-square" alt="License">
</p>

## About

NotEnoughChats is a lightweight command-line chat application built with C# and .NET 8.

The application uses Firebase Anonymous Authentication and Firebase Realtime Database to provide real-time public and private messaging directly from the terminal.

## Features

* Firebase Anonymous Authentication
* Firebase Realtime Database
* Guest usernames
* Unique NotEnoughChats IDs
* Real-time messaging
* Public chat
* Private messaging
* Friends system
* Friend requests
* Image upload
* Image download
* Image preview
* Command-line interface
* Lightweight and simple

## Requirements

* Windows
* .NET 8
* Visual Studio 2026
* Firebase project with Firebase Realtime Database
* Firebase Anonymous Authentication enabled

## Setup

Clone the repository:

```bash
git clone https://github.com/w1spo/notenoughchats.git
cd notenoughchats
```

Open the project in Visual Studio 2026 and build the solution.

Make sure your Firebase project is configured with:

* Firebase Anonymous Authentication
* Firebase Realtime Database
* Appropriate Realtime Database security rules

## Configuration

NotEnoughChats communicates directly with Firebase using its public client configuration.

Configure the Firebase project settings used by the application before running it.

Do not add Firebase service account credentials or private keys to the application.

## Build

Build the project in Release mode:

```bash
dotnet build -c Release
```

To create a self-contained Windows x64 release:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The published application will be located in:

```text
bin/Release/net8.0/win-x64/publish/
```

A self-contained build includes the required .NET runtime, so the target computer does not need to have the .NET 8 Desktop Runtime installed separately.

## Commands

NotEnoughChats provides several commands directly from the terminal:

```text
/help
/upload
/upload <path>
/view <id>
/download <id>
/clear
/quit
/id
/friends
/requests
/add #00001
/accept #00001
/msg #00001
/public
```

## Firebase

NotEnoughChats uses:

* Firebase Anonymous Authentication
* Firebase Realtime Database

Firebase is responsible for authentication and storing application data such as users, messages, friends, friend requests and uploaded images.

## Security

Never include Firebase service account credentials, private keys or other server-side secrets in the application.

The Firebase client API key is not a secret credential by itself, but access to the database should be protected using appropriate Firebase Realtime Database Security Rules.

## License

This project is licensed under the terms of the license included in this repository.

## Author

Created by [w1spo](https://github.com/w1spo).

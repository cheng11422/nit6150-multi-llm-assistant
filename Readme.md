# Multi-LLM Project Assistant

This repository contains the source code for a WPF desktop application that supports project-based interaction with multiple large language model providers, including OpenAI and Gemini.

## Project Structure

- `MultiLLMProjectAssistant.UI.slnx`: solution file
- `src/MultiLLMProjectAssistant.UI/`: main WPF application source
- `src/MultiLLMProjectAssistant.UI/Views/`: UI screens and view logic
- `src/MultiLLMProjectAssistant.UI/Services/`: supporting services for memory and file storage
- `src/MultiLLMProjectAssistant.UI/Models/`: application models

## Main Features

- Multi-provider request workflow for OpenAI and Gemini
- Backend connector for request validation, provider routing, and response handling
- Encrypted local API key storage
- Project-aware request logging and persistence
- Project memory support for improved request context
- File and template management inside the desktop application

## Backend and LLM Connector Scope

The backend and connector portion of the system includes:

- `LLMConnector.cs`
- request submission and response handling in `RequestBuilderView.xaml.cs`
- settings and encrypted API key management in `SettingsAndApiKeysView.xaml.cs`
- request log persistence in `RequestLogView.xaml.cs`
- project-scoped state and persistence logic in `ProjectSelectionView.xaml.cs`

## Requirements

- Windows
- .NET 8 SDK
- Visual Studio 2022 or another IDE that supports WPF and .NET 8

## Opening the Project

1. Open `MultiLLMProjectAssistant.UI.slnx`
2. Restore dependencies if prompted
3. Build and run the WPF application

## Notes

- Build output and Visual Studio cache folders are excluded by `.gitignore`
- Local runtime data and API-related JSON files should not be committed
- Keep the original folder structure when uploading to GitHub so the project remains buildable

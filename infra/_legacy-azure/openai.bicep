// openai.bicep — NO USADO
// Este proyecto usa OpenAI directo (api.openai.com) en lugar de Azure OpenAI
// porque Azure OpenAI requiere suscripción de pago con aprobación manual.
//
// La API Key de OpenAI se configura via:
//   - Development: dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
//   - Production:  secret en Azure Key Vault → referenciado desde Container App

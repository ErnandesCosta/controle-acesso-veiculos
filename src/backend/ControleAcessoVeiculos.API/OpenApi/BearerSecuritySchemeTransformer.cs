using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ControleAcessoVeiculos.API.OpenApi;

internal sealed class BearerSecuritySchemeTransformer(
    IAuthenticationSchemeProvider authenticationSchemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Controle de Acesso de Veículos API",
            Version = "v1",
            Description =
                "Contrato técnico do MVP para desenvolvimento e testes manuais com dados fictícios."
        };

        var authenticationSchemes =
            await authenticationSchemeProvider.GetAllSchemesAsync();
        if (!authenticationSchemes.Any(scheme => scheme.Name == "Bearer"))
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes =
            new Dictionary<string, IOpenApiSecurityScheme>
            {
                ["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    In = ParameterLocation.Header,
                    BearerFormat = "JWT",
                    Description =
                        "Informe somente o access token retornado por POST /auth/login."
                }
            };
    }
}

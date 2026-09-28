using ControleAcessoVeiculos.Domain.Entities;
using ControleAcessoVeiculos.Domain.Enums;

namespace ControleAcessoVeiculos.Domain.Tests;

public sealed class SessaoAutenticacaoTests
{
    private static readonly DateTime Now =
        new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NovaSessaoPodeSerRenovadaDentroDosLimites()
    {
        var session = CreateSession();

        Assert.True(session.PodeSerRenovada(
            Now.AddMinutes(14).AddSeconds(59),
            TimeSpan.FromMinutes(15)));
        Assert.False(session.FoiConsumida);
        Assert.False(session.FoiRevogada);
    }

    [Fact]
    public void SessaoExpiraNoLimiteDeInatividade()
    {
        var session = CreateSession();

        Assert.False(session.PodeSerRenovada(
            Now.AddMinutes(15),
            TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void SessaoNaoUltrapassaExpiracaoAbsoluta()
    {
        var session = CreateSession(absoluteLifetime: TimeSpan.FromHours(12));

        Assert.False(session.PodeSerRenovada(
            Now.AddHours(12),
            TimeSpan.FromHours(13)));
    }

    [Fact]
    public void RotacaoConsomeTokenAtualEPreservaFamiliaEExpiracao()
    {
        var session = CreateSession();
        var rotatedAt = Now.AddMinutes(10);

        var successor = session.Rotacionar(
            "successor-token-hash",
            rotatedAt,
            TimeSpan.FromMinutes(15));

        Assert.True(session.FoiConsumida);
        Assert.True(session.FoiRevogada);
        Assert.Equal(rotatedAt, session.ConsumidaEm);
        Assert.Equal(MotivoRevogacaoSessao.Rotacao, session.MotivoRevogacao);
        Assert.Equal("successor-token-hash", session.TokenSubstitutoHash);
        Assert.Equal(session.UsuarioId, successor.UsuarioId);
        Assert.Equal(session.FamiliaId, successor.FamiliaId);
        Assert.Equal(session.ExpiraEm, successor.ExpiraEm);
        Assert.Equal(rotatedAt, successor.UltimaAtividadeEm);
        Assert.True(successor.PodeSerRenovada(
            rotatedAt.AddMinutes(14).AddSeconds(59),
            TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void TokenConsumidoNaoPodeSerRotacionadoNovamente()
    {
        var session = CreateSession();
        session.Rotacionar(
            "first-successor-hash",
            Now.AddMinutes(1),
            TimeSpan.FromHours(1));

        Assert.Throws<InvalidOperationException>(() => session.Rotacionar(
            "second-successor-hash",
            Now.AddMinutes(2),
            TimeSpan.FromHours(1)));
    }

    [Fact]
    public void RotacaoExigeUmTokenSucessorDiferente()
    {
        var session = CreateSession();

        Assert.Throws<ArgumentException>(() => session.Rotacionar(
            session.TokenHash,
            Now.AddMinutes(1),
            TimeSpan.FromHours(1)));
    }

    [Fact]
    public void RevogacaoEhIdempotenteEPreservaMotivoOriginal()
    {
        var session = CreateSession();

        session.Revogar(Now.AddMinutes(1), MotivoRevogacaoSessao.Logout);
        session.Revogar(
            Now.AddMinutes(2),
            MotivoRevogacaoSessao.ReutilizacaoDetectada);

        Assert.Equal(Now.AddMinutes(1), session.RevogadaEm);
        Assert.Equal(MotivoRevogacaoSessao.Logout, session.MotivoRevogacao);
    }

    [Fact]
    public void RotacaoNaoPodeSerUsadaComoRevogacaoSemSucessor()
    {
        var session = CreateSession();

        Assert.Throws<ArgumentException>(() => session.Revogar(
            Now.AddMinutes(1),
            MotivoRevogacaoSessao.Rotacao));
    }

    private static SessaoAutenticacao CreateSession(
        TimeSpan? absoluteLifetime = null) =>
        new(
            10,
            Guid.Parse("4a1b1ba4-d17f-4726-845a-fbc916b09698"),
            "current-token-hash",
            Now,
            Now.Add(absoluteLifetime ?? TimeSpan.FromHours(12)));
}

using InPatient.Api.Hubs;
using InPatient.Api.Jobs;
using InPatient.Api.Services;
using InPatient.Api.Services.Ai;

namespace InPatient.Api;

public static class ServiceRegistration
{
    public static IServiceCollection AddInPatientServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<INotifier, SignalRNotifier>();
        services.Configure<StorageOptions>(config.GetSection("Storage"));
        services.AddSingleton<IFileStore, AzureBlobFileStore>();
        services.AddSingleton<FileLinkSigner>();
        services.AddSingleton<AlertExplanationQueue>();

        services.AddScoped<CareTeamService>();
        services.AddScoped<PatientFlagService>();
        services.AddScoped<BedLifecycle>();
        services.AddScoped<AdmissionService>();
        services.AddScoped<BedService>();
        services.AddScoped<WardHistoryService>();
        services.AddScoped<FileService>();
        services.AddScoped<AlertService>();
        services.AddScoped<PrescriptionService>();
        services.AddScoped<MedicationService>();
        services.AddScoped<MonitoringService>();
        services.AddScoped<ClinicalService>();
        services.AddScoped<WorklistService>();
        services.AddScoped<ClinicalContext>();
        services.AddScoped<VoiceService>();
        services.AddScoped<ReportService>();
        services.AddScoped<AiProgressService>();
        services.AddScoped<HandoverService>();
        services.AddScoped<DischargeService>();
        services.AddScoped<VoiceEvalService>();
        services.AddScoped<PatientPortalService>();
        services.AddScoped<AdminService>();

        services.Configure<AiOptions>(config.GetSection("Ai"));
        services.AddSingleton<IStructuredAi, OpenAiStructuredAi>();
        services.AddSingleton<ISpeechToText, OpenAiSpeechToText>();
        services.AddHostedService<AlertExplanationWorker>();
        services.AddHostedService<DevSeeder>();

        services.AddHostedService<DoseReminderJob>();
        services.AddHostedService<MissedDoseJob>();
        services.AddHostedService<AlertEscalationJob>();
        return services;
    }

    public static WebApplication MapInPatientHubs(this WebApplication app)
    {
        app.MapHub<WardHub>("/hubs/ward");
        return app;
    }
}

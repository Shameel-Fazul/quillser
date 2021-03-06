using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Quillser.Repositories;
using Quillser.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;

namespace Quillser
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            BsonSerializer.RegisterSerializer(new GuidSerializer(MongoDB.Bson.BsonType.String)); // Tells mongodb to serialize Id as string everytime it sees Guid in our entity
            BsonSerializer.RegisterSerializer(new DateTimeOffsetSerializer(MongoDB.Bson.BsonType.String)); // ^ Same with DateTimeOffSet
            var mongoDbSettings = Configuration.GetSection(nameof(MongoDbSettings)).Get<MongoDbSettings>();

            services.AddSingleton<IMongoClient>(serviceProvider =>
            {
                
                return new MongoClient(mongoDbSettings.ConnectionString);

            });
            services.AddSingleton<IRepository, InMongoDB>();
            services.AddControllers(options =>
            {
                options.SuppressAsyncSuffixInActionNames = false;
            });
            services.AddHealthChecks().AddMongoDb(mongoDbSettings.ConnectionString, name: "mongodb", timeout: TimeSpan.FromSeconds(3), tags: new[] {"ready"});
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseHttpsRedirection();
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions 
                { 
                    Predicate = (check) => check.Tags.Contains("ready"),
                    ResponseWriter = async(context, report) =>
                    {
                        var result = JsonSerializer.Serialize(
                            new
                            {
                                status = report.Status.ToString(),
                                checks = report.Entries.Select(entry => new 
                                {
                                    name = entry.Key,
                                    status = entry.Value.Status.ToString(),
                                    exception = entry.Value.Exception != null ? entry.Value.Exception.Message : "none",
                                    duration = entry.Value.Duration.ToString()
                                })
                            }
                        );

                        context.Response.ContentType = MediaTypeNames.Application.Json; // renders status in json
                        await context.Response.WriteAsync(result);
                    }
                });
                endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
                {
                    Predicate = (_) => false // for nothing - no tags- just api service
                });
            });
        }
    }
}

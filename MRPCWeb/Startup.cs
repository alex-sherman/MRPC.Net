using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MRPC.Net;
using Replicate.MetaData;
using Replicate.Serialization;
using Replicate.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MRPCWeb {
    public class Startup {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services) {
            ReplicationModel.Default.LoadTypes();
            ReplicationModel.Default.Add(typeof(Request));
            ReplicationModel.Default.Add(typeof(Response));
            ReplicationModel.Default[typeof(Guid)]
                .SetSurrogate(Surrogate.Simple<Guid, string>(g => g.ToString(), Guid.Parse));
            var client = new Client(50123);
            client.Listen();
            services.AddSingleton(client);
            services.AddTransient<IReplicateSerializer>((_) => new JSONSerializer(ReplicationModel.Default,
                new JSONSerializer.Configuration() { Strict = false }));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) {
            if (env.IsDevelopment()) {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseErrorHandling(app.ApplicationServices.GetRequiredService<IReplicateSerializer>());
            app.UseEndpoints(env, ReplicationModel.Default);
        }
    }
}

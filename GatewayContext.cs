using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace paymenu.Server.PaymenuGateway
{
    public partial class GatewayContext : DbContext
    {
        public GatewayContext()
        {
        }

        public GatewayContext(DbContextOptions<GatewayContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Logins> Logins { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer("Name=DefaultConnection");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("ProductVersion", "2.2.3-servicing-35854");

            modelBuilder.Entity<Logins>(entity =>
            {
                entity.HasKey(e => e.ApiKey)
                    .HasName("PK_Api_key");

                entity.ToTable("logins");

                entity.Property(e => e.ApiKey)
                    .HasColumnName("api_key")
                    .HasMaxLength(50)
                    .ValueGeneratedNever();

                entity.Property(e => e.Email)
                    .IsRequired()
                    .HasColumnName("email")
                    .HasMaxLength(50);

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();
            });
        }
    }
}

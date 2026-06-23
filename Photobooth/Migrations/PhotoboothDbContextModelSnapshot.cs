using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Photobooth.Data;

#nullable disable

namespace Photobooth.Migrations
{
    [DbContext(typeof(PhotoboothDbContext))]
    partial class PhotoboothDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "9.0.0");

            modelBuilder.Entity("Photobooth.Data.Models.Event", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd()
                    .HasColumnType("INTEGER");
                b.Property<string>("AccentColor").HasMaxLength(20).HasColumnType("TEXT");
                b.Property<DateTime?>("ArchivedAt").HasColumnType("TEXT");
                b.Property<string>("BackgroundColor").HasMaxLength(20).HasColumnType("TEXT");
                b.Property<string>("BackgroundImagePath").HasColumnType("TEXT");
                b.Property<DateTime>("CreatedAt").HasColumnType("TEXT");
                b.Property<string>("GreetingEyebrow").HasMaxLength(200).HasColumnType("TEXT");
                b.Property<string>("GreetingSubtitle").HasMaxLength(500).HasColumnType("TEXT");
                b.Property<string>("GreetingTitle").HasMaxLength(200).HasColumnType("TEXT");
                b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
                b.Property<bool>("PaywallEnabled").HasColumnType("INTEGER");
                b.Property<int?>("PrintLimitPerEvent").HasColumnType("INTEGER");
                b.Property<int?>("PrintLimitPerSession").HasColumnType("INTEGER");
                b.Property<string>("PhotostripTemplatePath").HasColumnType("TEXT");
                b.Property<bool>("SaveImagesEnabled").HasColumnType("INTEGER");
                b.Property<string>("Slug").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
                b.Property<string>("SurfaceColor").HasMaxLength(20).HasColumnType("TEXT");
                b.Property<string>("NavColor").HasMaxLength(20).HasColumnType("TEXT");
                b.HasKey("Id");
                b.HasIndex("Slug").IsUnique();
                b.ToTable("Events");
            });

            modelBuilder.Entity("Photobooth.Data.Models.Session", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<DateTime>("CreatedAt").HasColumnType("TEXT");
                b.Property<int>("EventId").HasColumnType("INTEGER");
                b.Property<int>("PrintCount").HasColumnType("INTEGER");
                b.HasKey("Id");
                b.HasIndex("EventId");
                b.ToTable("Sessions");
            });

            modelBuilder.Entity("Photobooth.Data.Models.Photo", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<string>("FilePath").HasColumnType("TEXT");
                b.Property<bool>("IsEnhanced").HasColumnType("INTEGER");
                b.Property<int>("Sequence").HasColumnType("INTEGER");
                b.Property<int>("SessionId").HasColumnType("INTEGER");
                b.HasKey("Id");
                b.HasIndex("SessionId");
                b.ToTable("Photos");
            });

            modelBuilder.Entity("Photobooth.Data.Models.EnhancedVariant", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<DateTime>("CreatedAt").HasColumnType("TEXT");
                b.Property<string>("FilePath").IsRequired().HasColumnType("TEXT");
                b.Property<int>("PhotoId").HasColumnType("INTEGER");
                b.Property<string>("StyleId").IsRequired().HasColumnType("TEXT");
                b.Property<string>("StyleName").IsRequired().HasColumnType("TEXT");
                b.HasKey("Id");
                b.HasIndex("PhotoId", "StyleId").IsUnique();
                b.ToTable("EnhancedVariants");
            });

            modelBuilder.Entity("Photobooth.Data.Models.Session", b =>
            {
                b.HasOne("Photobooth.Data.Models.Event", "Event")
                    .WithMany("Sessions")
                    .HasForeignKey("EventId")
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired();
                b.Navigation("Event");
            });

            modelBuilder.Entity("Photobooth.Data.Models.Photo", b =>
            {
                b.HasOne("Photobooth.Data.Models.Session", "Session")
                    .WithMany("Photos")
                    .HasForeignKey("SessionId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
                b.Navigation("Session");
            });

            modelBuilder.Entity("Photobooth.Data.Models.EnhancedVariant", b =>
            {
                b.HasOne("Photobooth.Data.Models.Photo", "Photo")
                    .WithMany("EnhancedVariants")
                    .HasForeignKey("PhotoId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
                b.Navigation("Photo");
            });

            modelBuilder.Entity("Photobooth.Data.Models.Event", b =>
            {
                b.Navigation("Sessions");
            });

            modelBuilder.Entity("Photobooth.Data.Models.Session", b =>
            {
                b.Navigation("Photos");
            });

            modelBuilder.Entity("Photobooth.Data.Models.Photo", b =>
            {
                b.Navigation("EnhancedVariants");
            });
#pragma warning restore 612, 618
        }
    }
}

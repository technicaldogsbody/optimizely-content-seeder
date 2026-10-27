using System.Linq;
using EPiServer.DataAbstraction;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.ServiceLocation;
using EPiServer.Web;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo;

/// <summary>
/// Registers Visual Builder Styles (display templates) so editors can pick style variants
/// in the Visual Builder UI. Each template's settings are read at render time by the
/// corresponding view via ViewData.TryGetCompositionRenderingNode().
/// </summary>
[ModuleDependency(typeof(InitializationModule))]
public class VisualBuilderStyleInit : IInitializableModule
{
    public void Initialize(InitializationEngine context)
    {
        var repo = context.Services.GetInstance<IDisplayTemplateRepository>();
        var contentTypes = context.Services.GetInstance<IContentTypeRepository>();

        Register(repo, contentTypes, typeof(HeroSection), "heroDefault", "Default Hero", template =>
        {
            AddSelect(template, "overlay", "Overlay", ("dark", "Dark"), ("light", "Light"), ("none", "None"));
            AddSelect(template, "height", "Height", ("full", "Full Screen"), ("large", "Large"), ("medium", "Medium"));
        });

        Register(repo, contentTypes, typeof(FeatureCardsSection), "featureDefault", "Default Features", template =>
        {
            AddSelect(template, "cardStyle", "Card Style", ("elevated", "Elevated"), ("flat", "Flat"),
                ("bordered", "Bordered"));
            AddSelect(template, "textAlign", "Text Alignment", ("left", "Left"), ("center", "Center"),
                ("right", "Right"));
        });

        Register(repo, contentTypes, typeof(CTASection), "ctaDefault", "Default CTA", template =>
        {
            AddSelect(template, "bgStyle", "Background", ("gradient", "Gradient"), ("solid", "Solid Color"),
                ("image", "Image"));
            AddSelect(template, "textColor", "Text Color", ("light", "Light"), ("dark", "Dark"));
        });

        Register(repo, contentTypes, typeof(ButtonElementBlock), "buttonDefault", "Default Button", template =>
        {
            AddSelect(template, "variant", "Variant", ("primary", "Primary"), ("secondary", "Secondary"),
                ("tertiary", "Tertiary"), ("danger", "Danger"));
            AddSelect(template, "size", "Size", ("small", "Small"), ("medium", "Medium"), ("large", "Large"));
        });

        Register(repo, contentTypes, typeof(RichTextElementBlock), "richTextDefault", "Default Rich Text", template =>
        {
            AddSelect(template, "fontSize", "Font Size", ("small", "Small"), ("normal", "Normal"), ("large", "Large"));
            AddSelect(template, "alignment", "Alignment", ("left", "Left"), ("center", "Center"), ("right", "Right"));
        });

        Register(repo, contentTypes, typeof(ImageElementBlock), "imageDefault", "Default Image", template =>
        {
            AddSelect(template, "borderRadius", "Border Radius", ("none", "None"), ("small", "Small"),
                ("medium", "Medium"), ("full", "Full Circle"));
            AddSelect(template, "shadow", "Shadow", ("none", "None"), ("small", "Small"), ("large", "Large"));
        });

        Register(repo, contentTypes, typeof(VideoBlock), "videoDefault", "Default Video", template =>
        {
            AddSelect(template, "aspectRatio", "Aspect Ratio", ("widescreen", "16:9"), ("standard", "4:3"),
                ("square", "1:1"));
            AddCheckbox(template, "autoplay", "Autoplay");
        });

        Register(repo, contentTypes, typeof(TestimonialElementBlock), "testimonialDefault", "Default Testimonial",
            template =>
            {
                AddSelect(template, "layout", "Layout", ("withPhoto", "With Photo"), ("withoutPhoto", "Without Photo"));
                AddCheckbox(template, "rating", "Show Rating");
            });

        Register(repo, contentTypes, typeof(StatisticElementBlock), "statDefault", "Default Statistic", template =>
        {
            AddSelect(template, "numberColor", "Number Color", ("primary", "Primary Blue"),
                ("success", "Success Green"), ("danger", "Danger Red"));
            AddSelect(template, "size", "Size", ("small", "Small"), ("large", "Large"));
        });
    }

    private static void Register(
        IDisplayTemplateRepository repo,
        IContentTypeRepository contentTypes,
        Type contentType,
        string key,
        string name,
        Action<DisplayTemplate> configure)
    {
        int contentTypeId = contentTypes.Load(contentType).ID;
        var existing = repo.List().FirstOrDefault(t => t.Key == key);

        var template = existing?.CreateWritableClone() ?? new DisplayTemplate { Key = key };
        template.Name = name;
        template.ContentTypeID = contentTypeId;
        template.IsDefault = true;
        template.Settings.Clear();

        configure(template);

        repo.Save(template);
    }

    private static void AddSelect(DisplayTemplate template, string key, string name,
        params (string Key, string Name)[] choices)
    {
        var setting = new DisplaySetting
        {
            Key = key,
            Name = name,
            Editor = DisplaySettingEditor.Select
        };

        foreach (var (choiceKey, choiceName) in choices)
        {
            setting.Choices.Add(new DisplaySettingChoice { Key = choiceKey, Name = choiceName });
        }

        template.Settings.Add(setting);
    }

    private static void AddCheckbox(DisplayTemplate template, string key, string name)
    {
        template.Settings.Add(new DisplaySetting
        {
            Key = key,
            Name = name,
            Editor = DisplaySettingEditor.Checkbox
        });
    }

    public void Uninitialize(InitializationEngine context)
    {
    }
}

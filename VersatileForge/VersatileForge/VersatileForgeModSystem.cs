using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VersatileForge;

[HarmonyPatch]
public class VersatileForgeModSystem : ModSystem
{
    private static string HOTKEY_CODE = "changerecipebutton";

    private static ICoreClientAPI _capi;
    private Harmony _harmony;

    public override void Start(ICoreAPI api)
    {
        _harmony = new Harmony("versatileforge");
        _harmony.PatchAll();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;
        api.Input.RegisterHotKey(HOTKEY_CODE, Lang.Get("versatileforge:settings-changerecipe-button"), GlKeys.R, HotkeyType.GUIOrOtherControls);
    }


    // BlockAnvil
    [HarmonyReversePatch]
    [HarmonyPatch(typeof(Block), nameof(Block.OnLoaded))]
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void BaseOnLoadedDummy(BlockAnvil __instance, ICoreAPI api)
    {
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockAnvil), nameof(BlockAnvil.OnLoaded))]
    public static bool OnLoaded(BlockAnvil __instance, ICoreAPI api)
    {
        BaseOnLoadedDummy(__instance, api);

        if (api.Side != EnumAppSide.Client) return false;
        ICoreClientAPI capi = api as ICoreClientAPI;

        Dictionary<string, MetalPropertyVariant> metalsByCode = new Dictionary<string, MetalPropertyVariant>();

        MetalProperty metals = api.Assets.TryGet("worldproperties/block/metal.json").ToObject<MetalProperty>();
        for (int i = 0; i < metals.Variants.Length; i++)
        {
            // Metals currently don't have a domain
            metalsByCode[metals.Variants[i].Code.Path] = metals.Variants[i];
        }

        string metalType = __instance.LastCodePart();
        int ownMetalTier = 0;
        if (metalsByCode.TryGetValue(metalType, out var value)) ownMetalTier = value.Tier;

        var interactions = ObjectCacheUtil.GetOrCreate(api, "anvilBlockInteractions" + ownMetalTier, () =>
        {
            List<ItemStack> workableStacklist = new List<ItemStack>();
            List<ItemStack> hammerStacklist = new List<ItemStack>();


            bool viableTier = metalsByCode.ContainsKey(metalType) && metalsByCode[metalType].Tier <= ownMetalTier + 1;
            foreach (Item item in api.World.Items)
            {
                if (item.Code == null) continue;

                if (item is ItemIngot && viableTier)
                {
                    workableStacklist.Add(new ItemStack(item));
                }

                if (item is ItemHammer)
                {
                    hammerStacklist.Add(new ItemStack(item));
                }
            }

            return new WorldInteraction[]
            {
                new WorldInteraction()
                {
                    ActionLangCode = "blockhelp-anvil-takeworkable",
                    HotKeyCode = null,
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (wi, bs, es) =>
                    {
                        BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
                        return bea?.WorkItemStack != null;
                    }
                },
                new WorldInteraction()
                {
                    ActionLangCode = "blockhelp-anvil-placeworkable",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = workableStacklist.ToArray(),
                    GetMatchingStacks = (wi, bs, es) =>
                    {
                        BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
                        return bea?.WorkItemStack == null ? wi.Itemstacks : null;
                    }
                },
                new WorldInteraction()
                {
                    ActionLangCode = "blockhelp-anvil-smith",
                    MouseButton = EnumMouseButton.Left,
                    Itemstacks = hammerStacklist.ToArray(),
                    GetMatchingStacks = (wi, bs, es) =>
                    {
                        BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
                        return bea?.WorkItemStack == null ? null : wi.Itemstacks;
                    }
                },
                new WorldInteraction()
                {
                    ActionLangCode = "blockhelp-anvil-rotateworkitem",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = hammerStacklist.ToArray(),
                    GetMatchingStacks = (wi, bs, es) =>
                    {
                        BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
                        return bea?.WorkItemStack == null ? null : wi.Itemstacks;
                    }
                },
                new WorldInteraction()
                {
                    ActionLangCode = "blockhelp-selecttoolmode",
                    HotKeyCode = "toolmodeselect",
                    MouseButton = EnumMouseButton.None,
                    Itemstacks = hammerStacklist.ToArray(),
                    GetMatchingStacks = (wi, bs, es) =>
                    {
                        BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
                        return bea?.WorkItemStack == null ? null : wi.Itemstacks;
                    }
                },
                new WorldInteraction()
                {
                    ActionLangCode = "blockhelp-anvil-addvoxels",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = workableStacklist.ToArray(),
                    GetMatchingStacks = (wi, bs, es) =>
                    {
                        BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
                        return bea?.WorkItemStack == null
                            ? null
                            : new ItemStack[] { (bea.WorkItemStack.Collectible as IAnvilWorkable).GetBaseMaterial(bea.WorkItemStack) };
                    }
                },
                new WorldInteraction()
                {
                    ActionLangCode = "versatileforge:blockhelp-anvil-changerecipe",
                    HotKeyCode = HOTKEY_CODE,
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (wi, bs, es) =>
                    {
                        BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityAnvil;
                        return bea?.WorkItemStack != null;
                    }
                }
            };
        });

        Traverse.Create(__instance).Field("interactions").SetValue(interactions);
        return false;
    }

    // BlockEntityAnvil
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockEntityAnvil), "OnPlayerInteract")]
    public static bool OnPlayerInteract(BlockEntityAnvil __instance, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!(world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityAnvil blockEntity) || _capi == null)
            return true;

        bool isHotKeyPressed = _capi.Input.KeyboardKeyState[_capi.Input.GetHotKeyByCode(HOTKEY_CODE).CurrentMapping.KeyCode];
        if (isHotKeyPressed && __instance.WorkItemStack != null)
        {
            var collectible = __instance.WorkItemStack.Collectible as IAnvilWorkable;
            List<SmithingRecipe> matchingRecipes = collectible.GetMatchingRecipes(__instance.WorkItemStack);

            if (matchingRecipes.Count == 1)
                return false;
            
            MethodInfo methodInfo = typeof(BlockEntityAnvil).GetMethod("OpenDialog", BindingFlags.NonPublic | BindingFlags.Instance);
            methodInfo.Invoke(__instance, new object[] { __instance.WorkItemStack });
            return false;
        }

        return true;
    }
}
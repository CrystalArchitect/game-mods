using System;
using System.Collections;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using KneeSlapperFreeze;

[BepInPlugin("local.valheim.freezetest", "Freeze Integration Check", "1.0.0")]
[BepInDependency(Plugin.Guid)]
public sealed class IntegrationCheck : BaseUnityPlugin
{
    private void Start()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-gathering-catalog") >= 0)
            StartCoroutine(Catalog());
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-freeze-integration-check") >= 0)
            StartCoroutine(RunGuarded());
    }

    private IEnumerator Catalog()
    {
        yield return new WaitForSeconds(12f);
        foreach (var prefab in ObjectDB.instance.m_items)
        {
            var item = prefab.GetComponent<ItemDrop>()?.m_itemData;
            if (item == null) continue;
            var shared = item.m_shared;
            if (shared.m_skillType != Skills.SkillType.Bows && shared.m_skillType != Skills.SkillType.Axes
                && shared.m_skillType != Skills.SkillType.Pickaxes && shared.m_itemType != ItemDrop.ItemData.ItemType.Tool
                && !prefab.name.Contains("Fishing") && !prefab.name.Contains("Scythe") && !prefab.name.Contains("Cultivator")) continue;
            var damage = item.GetDamage(shared.m_maxQuality, 0f);
            Logger.LogInfo($"CATALOG|{prefab.name}|{Localization.instance.Localize(shared.m_name)}|quality={shared.m_maxQuality}|tier={shared.m_toolTier}|type={shared.m_itemType}|skill={shared.m_skillType}|damage={damage.GetTotalDamage()}|chop={damage.m_chop}|pick={damage.m_pickaxe}|weight={item.GetWeight()}");
        }
        Application.Quit();
    }

    private IEnumerator RunGuarded()
    {
        var test = Run();
        while (true)
        {
            object next;
            try
            {
                if (!test.MoveNext()) break;
                next = test.Current;
            }
            catch (Exception ex)
            {
                Logger.LogError("FREEZE_TEST_FAIL: " + ex);
                Application.Quit();
                yield break;
            }
            yield return next;
        }
        Logger.LogInfo("FREEZE_TEST_ALL_PASS");
        Application.Quit();
    }

    private void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Logger.LogInfo("PASS: " + description);
    }

    private IEnumerator Run()
    {
        yield return new WaitForSeconds(12f);
        var args = Environment.GetCommandLineArgs();
        var saveIndex = Array.IndexOf(args, "-savedir");
        if (saveIndex < 0 || saveIndex + 1 >= args.Length) throw new Exception("Test save directory required");
        Utils.SetSaveDataPath(args[saveIndex + 1]);
        Check(Plugin.Matches("Knee-Slapper", "ArrowWood"), "intended character and ammunition match");
        Check(!Plugin.Matches("Other", "ArrowWood") && !Plugin.Matches("Knee-Slapper", "ArrowFire"), "other characters and ammunition excluded");
        var profile = new PlayerProfile("freeze_integration_only", FileHelpers.FileSource.Local);
        profile.SetName("Knee-Slapper");
        profile.m_firstSpawn = false;
        profile.Save();
        Game.SetProfile(profile.GetFilename(), FileHelpers.FileSource.Local);
        ZNet.SetServer(true, false, false, "FreezeIntegrationOnly", "", new World("FreezeIntegrationOnly", "freezetest"));
        ZNet.ResetServerHost();
        AccessTools.Method(typeof(FejdStartup), "LoadMainScene").Invoke(FejdStartup.instance, null);
        float timeout = Time.realtimeSinceStartup + 150f;
        while (!Player.m_localPlayer || !ZNetScene.instance || Player.m_localPlayer.InIntro())
        {
            if (Time.realtimeSinceStartup > timeout) throw new Exception("Timed out loading isolated test world");
            yield return null;
        }
        var player = Player.m_localPlayer;
        player.SetGodMode(true);
        if (AccessTools.TypeByName("ComfyQuickSlots.QuickSlotsManager") != null)
        {
            var inv = player.GetInventory();
            player.SetInventorySize(4);
            Check(inv.GetName() == "ComfyQuickSlotsInventory" && inv.GetHeight() >= 7, "Comfy equipment row and Better Archery quiver survive inventory resizing");
            var helmet = inv.AddItem("HelmetLeather", 1, 1, 0, 0L, "", false);
            Check(helmet != null && player.EquipItem(helmet, false) && helmet.m_gridPos == new Vector2i(0, 4), "Comfy places equipped helmet in reserved head slot");
            var gui = InventoryGui.instance;
            gui.Show(null, 1);
            yield return new WaitForSeconds(0.5f);
            var elements = (System.Collections.Generic.List<InventoryElement>)AccessTools.Field(typeof(InventoryGrid), "m_elements").GetValue(gui.m_playerGrid);
            Check(elements.Count >= 56 && elements[32].gameObject.activeSelf, "inventory opens with visible Comfy equipment row and separate quiver rows");
            Check(!string.IsNullOrEmpty(ItemDrop.ItemData.GetTooltip(helmet, 1, false, 1f, 0)), "Recycle N Reclaim tooltip patch executes");
            var cultivator = ObjectDB.instance.GetItemPrefab("Cultivator").GetComponent<ItemDrop>().m_itemData;
            var bush = cultivator.m_shared.m_buildPieces.m_pieces.FirstOrDefault(p => p.name.Contains("RaspberryBush"));
            Check(bush && bush.GetComponent<Piece>(), "Plant Everything registers plantable raspberry bush in cultivator");
            var planted = Instantiate(bush, player.transform.position + Vector3.right * 8f, Quaternion.identity);
            Check(planted.GetComponent<Pickable>(), "Plant Everything raspberry bush spawns with harvest component");
            ZNetScene.instance.Destroy(planted);
            var reclaimer = AccessTools.TypeByName("Recycle_N_Reclaim.GamePatches.Recycling.Reclaimer");
            var isolatedInventory = new Inventory("CompatibilityTest", null, 8, 4);
            isolatedInventory.AddItem("Club", 1, 1, 0, 0L, "", false);
            ((System.Collections.Generic.HashSet<string>)AccessTools.Field(typeof(Player), "m_knownRecipes").GetValue(player)).Add(ObjectDB.instance.GetItemPrefab("Club").GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
            var analysis = (IList)AccessTools.Method(reclaimer, "GetRecyclingAnalysisForInventory").Invoke(null, new object[] { isolatedInventory, player });
            Check(analysis.Count == 1, "Recycle N Reclaim analyzes a disposable club recipe");
            AccessTools.Method(reclaimer, "RecycleInventoryForAllRecipes").Invoke(null, new object[] { isolatedInventory, "CompatibilityTest", player });
            Check(!isolatedInventory.GetAllItems().Any(i => i.m_dropPrefab.name == "Club") && isolatedInventory.GetAllItems().Any(i => i.m_dropPrefab.name == "Wood"), "Recycle N Reclaim consumes disposable club and returns wood");
            gui.Hide();
            player.UnequipItem(helmet, false);
            inv.RemoveItem(helmet);
        }
        GatheringKit.Deliver(player);
        GatheringKit.MakeToolsIndestructible(player);
        Check(player.m_customData.ContainsKey(GatheringKit.DeliveredKey), "gathering kit delivered and marked once");
        var inventory = player.GetInventory();
        foreach (string name in GatheringKit.Equipment)
        {
            var item = inventory.GetAllItems().FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == name);
            Check(item != null && item.m_quality == (name == "Bow" ? 1 : item.m_shared.m_maxQuality), "kit item present at requested quality: " + name);
            if (name != "ArrowWood" && name != "FishingBait")
                Check(!item.m_shared.m_useDurability && item.m_durability == item.GetMaxDurability(), "unlimited durability: " + name);
        }
        var count = inventory.GetAllItems().Count;
        GatheringKit.Deliver(player);
        Check(count == inventory.GetAllItems().Count, "repeat delivery creates no duplicates");
        Check(player.GetCurrentWeapon().m_dropPrefab.name == "Bow" && player.GetAmmoItem().m_dropPrefab.name == "ArrowWood", "basic bow and wooden arrows equipped");
        Check(player.GetMaxCarryWeight() >= 10000f && !player.IsEncumbered(), "carrying capacity supports full kit");
        float stamina = player.GetStamina();
        player.UseStamina(10000f);
        Check(player.GetStamina() == stamina && player.HaveStamina(10000f), "stamina does not drain and suffices for any action");
        var bowTemplate = ObjectDB.instance.GetItemPrefab("Bow").GetComponent<ItemDrop>().m_itemData;
        Check(bowTemplate.m_shared.m_useDurability, "global bow prefab retains normal durability");
        var ammoAttack = new Attack();
        AccessTools.Field(typeof(Attack), "m_character").SetValue(ammoAttack, player);
        AccessTools.Field(typeof(Attack), "m_weapon").SetValue(ammoAttack, player.GetCurrentWeapon());
        var useAmmo = AccessTools.Method(typeof(Attack), "UseAmmo");
        var arrowsItem = player.GetAmmoItem();
        int arrowsCount = arrowsItem.m_stack;
        bool allShots = true;
        for (int shot = 0; shot < 200; shot++)
        {
            var argsShot = new object[] { null };
            allShots &= (bool)useAmmo.Invoke(ammoAttack, argsShot) && ReferenceEquals(argsShot[0], arrowsItem);
        }
        Check(allShots && arrowsItem.m_stack == arrowsCount && inventory.ContainsItem(arrowsItem), "200 ammo uses retain all wooden arrows");
        var playerZdo = player.GetComponent<ZNetView>().GetZDO();
        playerZdo.Set(ZDOVars.s_playerName, "OtherCharacter");
        var otherShot = new object[] { null };
        useAmmo.Invoke(ammoAttack, otherShot);
        Check(arrowsItem.m_stack == arrowsCount - 1 && player.GetMaxCarryWeight() < 10000f, "other character retains normal ammo consumption and capacity");
        playerZdo.Set(ZDOVars.s_playerName, "Knee-Slapper");
        Check(player.GetPlayerName() == "Knee-Slapper", "isolated test character loaded");
        Check(ObjectDB.instance.GetStatusEffect(Plugin.EffectHash), "custom status registered in game ObjectDB");
        var enemy = Instantiate(ZNetScene.instance.GetPrefab("Greydwarf"), player.transform.position + Vector3.right * 5f, Quaternion.identity).GetComponent<Character>();
        enemy.SetMaxHealth(10000f);
        yield return new WaitForSeconds(0.2f);
        var body = enemy.GetComponent<Rigidbody>();
        var constraints = body.constraints;
        var animator = enemy.GetComponentInChildren<Animator>();
        var wood = Ammo("ArrowWood");
        Hit(player, enemy, wood);
        yield return new WaitForSeconds(0.1f);
        Check(Plugin.IsFrozen(enemy), "wooden arrow collision applies freeze to enemy");
        Check(body.constraints == RigidbodyConstraints.FreezeAll && animator.speed == 0f && !enemy.CanMove(), "enemy physics, animation and movement are frozen");
        Check(!enemy.GetBaseAI().UpdateAI(0.02f), "enemy AI suspended");
        Check(!((Humanoid)enemy).StartAttack(player, false), "enemy cannot start an attack");
        var position = enemy.transform.position;
        yield return new WaitForSeconds(1f);
        Check(Vector3.Distance(position, enemy.transform.position) < 0.02f, "enemy remains stationary");
        Hit(player, enemy, wood);
        yield return new WaitForSeconds(1.1f);
        Check(Plugin.IsFrozen(enemy), "second hit refreshes freeze beyond original expiry");
        yield return new WaitForSeconds(1f);
        Check(!Plugin.IsFrozen(enemy), "freeze expires two seconds after last hit");
        Check(body.constraints == constraints && animator.speed > 0f, "physics and animation restore after thaw");
        Hit(player, enemy, Ammo("ArrowFire"));
        yield return new WaitForSeconds(0.1f);
        Check(!Plugin.IsFrozen(enemy), "fire arrow collision does not apply custom freeze");
        enemy.SetTamed(true);
        Hit(player, enemy, wood);
        yield return new WaitForSeconds(0.1f);
        Check(!Plugin.IsFrozen(enemy), "tamed creatures are excluded");
        float landingDeadline = Time.realtimeSinceStartup + 20f;
        while (!player.IsOnGround() || player.IsKnockedBack() || player.IsStaggering())
        {
            if (Time.realtimeSinceStartup > landingDeadline) throw new Exception("Timed out waiting for jump test landing");
            yield return null;
        }
        var playerBody = player.GetComponent<Rigidbody>();
        player.Jump();
        Check(playerBody.linearVelocity.y > 0f, "normal first jump works");
        yield return new WaitForSeconds(0.3f);
        Check(!player.IsOnGround(), "player is airborne before second jump");
        float beforeSecond = playerBody.linearVelocity.y;
        player.Jump();
        Check(playerBody.linearVelocity.y > beforeSecond + 1f, "second jump boosts upward velocity in midair");
        yield return new WaitForSeconds(0.3f);
        float beforeThird = playerBody.linearVelocity.y;
        player.Jump();
        Check(Mathf.Abs(playerBody.linearVelocity.y - beforeThird) < 0.001f, "third airborne jump is blocked");
        landingDeadline = Time.realtimeSinceStartup + 20f;
        while (!player.IsOnGround())
        {
            if (Time.realtimeSinceStartup > landingDeadline) throw new Exception("Timed out waiting to recharge double jump");
            yield return null;
        }
        yield return new WaitForFixedUpdate();
        player.Jump();
        yield return new WaitForSeconds(0.3f);
        playerZdo.Set(ZDOVars.s_playerName, "OtherCharacter");
        float beforeOther = playerBody.linearVelocity.y;
        player.Jump();
        Check(Mathf.Abs(playerBody.linearVelocity.y - beforeOther) < 0.001f, "other characters receive no airborne jump");
        playerZdo.Set(ZDOVars.s_playerName, "Knee-Slapper");
        player.Jump();
        Check(playerBody.linearVelocity.y > beforeOther + 1f, "landing restores Knee-Slapper's extra jump");
        float fallDamage = 100f;
        player.GetSEMan().ModifyFallDamage(100f, ref fallDamage);
        Check(fallDamage == 0f, "Knee-Slapper's fall damage resolves to zero");
        playerZdo.Set(ZDOVars.s_playerName, "OtherCharacter");
        fallDamage = 100f;
        player.GetSEMan().ModifyFallDamage(100f, ref fallDamage);
        Check(fallDamage == 100f, "other characters retain normal fall damage");
        playerZdo.Set(ZDOVars.s_playerName, "Knee-Slapper");
        landingDeadline = Time.realtimeSinceStartup + 20f;
        while (!player.IsOnGround())
        {
            if (Time.realtimeSinceStartup > landingDeadline) throw new Exception("Timed out before drop test");
            yield return null;
        }
        player.SetGodMode(false);
        player.SetHealth(player.GetMaxHealth());
        float healthBeforeDrop = player.GetHealth();
        playerBody.position += Vector3.up * 30f;
        player.ForceJump(Vector3.zero, false);
        yield return new WaitForSeconds(0.3f);
        Check(!player.IsOnGround(), "30-meter drop test starts airborne");
        landingDeadline = Time.realtimeSinceStartup + 20f;
        while (!player.IsOnGround())
        {
            if (Time.realtimeSinceStartup > landingDeadline) throw new Exception("Timed out during drop test");
            yield return null;
        }
        yield return new WaitForSeconds(0.1f);
        Check(!player.IsDead() && player.GetHealth() >= healthBeforeDrop, "30-meter fall causes no health loss with god mode disabled");
        float healthBeforeHit = player.GetHealth();
        var ordinaryHit = new HitData { m_hitType = HitData.HitType.EnemyHit, m_point = player.GetCenterPoint() };
        ordinaryHit.m_damage.m_damage = 3f;
        player.Damage(ordinaryHit);
        yield return new WaitForSeconds(0.1f);
        Check(player.GetHealth() < healthBeforeHit, "ordinary damage still applies");
        player.SetGodMode(true);
    }

    private void Hit(Player player, Character enemy, ItemDrop.ItemData ammo)
    {
        var projectile = Instantiate(ammo.m_shared.m_attack.m_attackProjectile, enemy.GetCenterPoint() + Vector3.up * 2f, Quaternion.identity).GetComponent<Projectile>();
        var hit = new HitData { m_skill = Skills.SkillType.Bows, m_blockable = false, m_dodgeable = false };
        hit.m_damage.m_pierce = 1f;
        hit.SetAttacker(player);
        var bow = ObjectDB.instance.GetItemPrefab("Bow").GetComponent<ItemDrop>().m_itemData;
        projectile.Setup(player, Vector3.forward * 10f, 0f, hit, bow, ammo);
        Logger.LogInfo("TEST_HIT: ammo=" + ammo.m_dropPrefab?.name + " hash=" + AccessTools.Field(typeof(Projectile), "m_statusEffectHash").GetValue(projectile) + " expected=" + Plugin.EffectHash + " enemy=" + BaseAI.IsEnemy(player, enemy) + " collider=" + enemy.GetComponent<Collider>());
        projectile.OnHit(enemy.GetComponent<Collider>(), enemy.GetCenterPoint(), false, Vector3.back);
    }

    private ItemDrop.ItemData Ammo(string name)
    {
        var prefab = ObjectDB.instance.GetItemPrefab(name);
        var ammo = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
        ammo.m_dropPrefab = prefab;
        return ammo;
    }
}

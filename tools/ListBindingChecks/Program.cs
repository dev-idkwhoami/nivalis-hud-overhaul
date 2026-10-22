using NivalisMods.HudOverhaul;
using Nivalis;
using Nivalis.UI;
using Nivalis.CraftingSystem;
using Nivalis.Locale;
using Nivalis.Locale.UI;
using Nivalis.GhostSystem.CustomerLoop;

var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
void Bound(ItemListUI list, Owner owner, object selected, string name)
{
    Check(ReferenceEquals(list.OriginalCallback,list.CurrentCallback), name + " retains original native callback");
    Check(list.SelectedIndex >= 0 && ReferenceEquals(list.Data[list.SelectedIndex], selected), name + " selection");
    list.BindAt!(list.SelectedIndex);
    Check(ReferenceEquals(owner.Bound, selected) && owner.BoundIndex == list.SelectedIndex && owner.BoundSelected, name + " selected callback");
    list.BindAt(list.Data.Length - 1);
    Check(ReferenceEquals(owner.Bound, list.Data[^1]) && owner.BoundIndex == list.Data.Length - 1, name + " off-screen callback");
}
var journal = new JournalUI();
for (var i=0; i<250; i++) journal._quests.Add(new RuntimeQuest { LastUpdateTime=i, IsActive=i%2==0 });
var quest = journal._quests[17]; journal.entriesListUi.SelectedIndex=17;
journal.entriesListUi.Seed<RuntimeQuest,JournalListItemUI>(journal._quests,journal,nameof(JournalUI.BindQuest));
DataListSorting.Journal(journal,new("updated",SortKey.Updated,true));
Check(journal._quests[0].LastUpdateTime==249 && journal._quests[249].LastUpdateTime==0,"all journal data sorted");
Bound(journal.entriesListUi,journal,quest,"journal");
DataListSorting.Journal(journal,new("active",SortKey.Active,true));
Check(journal._quests.Take(125).All(q=>q.IsActive) && journal._quests.Skip(125).All(q=>!q.IsActive),"active groups include off-screen data");
Bound(journal.entriesListUi,journal,quest,"journal second sort");
var vendor = new ShopUiVendorPanel();
for(var i=0;i<200;i++) vendor._entries.Add(new() { Item=new() { Type=new() { Comfort=i%10 } }, Identity=i.ToString() });
var product=vendor._entries[73]; vendor.itemsList.SelectedIndex=73;
vendor.itemsList.Seed<ShopUiVendorPanel.ShopEntry,ShopItemDisplayUI>(vendor._entries,vendor,nameof(ShopUiVendorPanel.BindEntry));
DataListSorting.Vendor(vendor,new("comfort",SortKey.Comfort,true));
Check(vendor._entries[0].Item.Type.Comfort==9 && vendor._entries[199].Item.Type.Comfort==0,"vendor full data order");
Check(vendor._entries.Where(e=>e.Item.Type.Comfort==9).Select(e=>e.Identity).SequenceEqual(Enumerable.Range(0,20).Select(i=>(9+10*i).ToString())),"vendor stable ties");
Bound(vendor.itemsList,vendor,product,"vendor");
var menu=new MenuModificationWindow();
for(var i=0;i<120;i++) { menu._listedRecipes.Add(new() { Calories=i,Exclusivity=120-i }); menu._listedMenuItems.Add(new(new MealMenuItem { Customized=new() { Calories=120-i, Exclusivity=i } },MenuItemDisplayUI.WarningType.Equipment)); }
var recipe=menu._listedRecipes[29]; menu.recipeItemList.SelectedIndex=29;
menu.recipeItemList.Seed<IRecipe,RecipeItemDisplayUI>(menu._listedRecipes,menu,nameof(MenuModificationWindow.BindRecipe));
DataListSorting.Recipes(menu,new("calories",SortKey.Calories,true));
Check(menu._listedRecipes[0].Calories==119,"recipe calories"); Bound(menu.recipeItemList,menu,recipe,"recipes");
DataListSorting.Recipes(menu,new("exclusive",SortKey.Exclusivity,true));
Check(menu._listedRecipes[0].Exclusivity==120,"recipe exclusivity"); Bound(menu.recipeItemList,menu,recipe,"recipes reversed");
var meal=menu._listedMenuItems[18]; menu.menuItemList.SelectedIndex=18;
menu.menuItemList.Seed<Il2CppSystem.ValueTuple<MealMenuItem,MenuItemDisplayUI.WarningType>,MenuItemDisplayUI>(menu._listedMenuItems,menu,nameof(MenuModificationWindow.BindMenuItem));
DataListSorting.Menu(menu,new("calories",SortKey.Calories));
Check(menu._listedMenuItems[0].Item1.Customized.Calories==1 && menu._listedMenuItems.All(e=>e.Item1.RequestedVenue==menu._venue && e.Item2==MenuItemDisplayUI.WarningType.Equipment),"customized menu data and warning preserved");
Bound(menu.menuItemList,menu,meal,"menu");
var reviews=new LocaleReviewOverviewTab();
for(var i=0;i<300;i++) reviews._listedReviews.Add(new() { ReviewTime=new() {TotalGameSeconds=i},Score=i%5,Positive=i%2==0 });
reviews.reviewList.Seed<Review,ReviewItemDisplayUi>(reviews._listedReviews,null,nameof(LocaleReviewOverviewTab.BindReview),typeof(LocaleReviewOverviewTab));
foreach(var key in new[] {SortKey.ReviewTime,SortKey.Stars,SortKey.Sentiment})
{
    DataListSorting.Reviews(reviews,new("reviews",key,true));
    reviews.reviewList.BindAt!(299);
    Check(ReferenceEquals(reviews.reviewList.OriginalCallback,reviews.reviewList.CurrentCallback),"review retains native callback");
    Check(ReferenceEquals(LocaleReviewOverviewTab.LastReview,reviews._listedReviews[299]),"native review static callback "+key);
    Check(key switch {SortKey.ReviewTime=>reviews._listedReviews[0].ReviewTime.TotalGameSeconds==299, SortKey.Stars=>reviews._listedReviews[0].Score==4,_=>reviews._listedReviews.Take(150).All(x=>x.Positive)},"native review order "+key);
}
var nativeRebinds=vendor.itemsList.Rebinds;
DataListSorting.Vendor(vendor,new("native",SortKey.Native));
Check(vendor.itemsList.Rebinds==nativeRebinds,"native sorting untouched");
// Shopping-style filters must remove data, remap selection, and support empty results.
var data=vendor._entries.ToArray();
var keep=Enumerable.Range(0,data.Length).Where(i=>i%2==0).ToArray();
vendor.itemsList.SelectedIndex=keep[20]; product=data[keep[20]];
DataListSorting.Replace<ShopUiVendorPanel.ShopEntry,ShopItemDisplayUI>(vendor.itemsList,vendor._entries,data,keep);
Check(vendor.itemsList.Data.Length==100 && vendor._entries.Count==100,"filter full data"); Bound(vendor.itemsList,vendor,product,"filtered");
data=vendor._entries.ToArray();vendor.itemsList.SelectedIndex=1;
DataListSorting.Replace<ShopUiVendorPanel.ShopEntry,ShopItemDisplayUI>(vendor.itemsList,vendor._entries,data,[0]);
Check(vendor.itemsList.SelectedIndex==-1 && vendor._entries.Count==1,"removed selection cleared");
data=vendor._entries.ToArray();
DataListSorting.Replace<ShopUiVendorPanel.ShopEntry,ShopItemDisplayUI>(vendor.itemsList,vendor._entries,data,[]);
Check(vendor.itemsList.SelectedIndex==-1 && vendor._entries.Count==0 && vendor.itemsList.Data.Length==0,"empty filter clears list");
Console.WriteLine($"Passed {checks} data-list binding checks using production adapters (managed native-API model).");

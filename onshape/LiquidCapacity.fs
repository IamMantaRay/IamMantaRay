FeatureScript 2716;
import(path : "onshape/std/geometry.fs", version : "2716.0");

/**
 * Liquid Capacity
 *
 * Measures how much liquid a container (cup, bottle, tank, ...) can hold.
 *
 * How it works:
 *   1. A box slightly larger than the container is built, from below the
 *      container's bottom up to the liquid level (measured along the "up"
 *      direction).
 *   2. The container is subtracted from that box. The box splits into pieces.
 *   3. Pieces that touch the box's sides or bottom are "outside air" and are
 *      discarded. Whatever remains is enclosed by the container walls, i.e.
 *      the liquid.
 *   4. The liquid volume is measured and reported (mL / L, optional mass).
 *
 * Works for open containers (cups, bowls) and closed ones (bottles, tanks).
 * The container itself is never modified.
 */

export enum FillMode
{
    annotation { "Name" : "Full (to the brim)" }
    FULL,
    annotation { "Name" : "Percent of cavity depth" }
    PERCENT,
    annotation { "Name" : "Liquid level (from container bottom)" }
    LEVEL
}

const FILL_PERCENT_BOUNDS = { (unitless) : [0, 80, 100] } as RealBoundSpec;
const DENSITY_BOUNDS = { (unitless) : [0, 1, 100] } as RealBoundSpec;

annotation { "Feature Type Name" : "Liquid Capacity",
             "Feature Name Template" : "Liquid capacity (#volume)",
             "Feature Type Description" : "Measures the liquid volume a container can hold" }
export const liquidCapacity = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Container", "Filter" : EntityType.BODY && BodyType.SOLID }
        definition.container is Query;

        annotation { "Name" : "Up direction (default: +Z)",
                     "Filter" : QueryFilterCompound.ALLOWS_AXIS || GeometryType.PLANE,
                     "MaxNumberOfPicks" : 1 }
        definition.upDirection is Query;

        annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flipUp is boolean;

        annotation { "Name" : "Fill mode" }
        definition.fillMode is FillMode;

        if (definition.fillMode == FillMode.PERCENT)
        {
            annotation { "Name" : "Fill (% of cavity depth)" }
            isReal(definition.fillPercent, FILL_PERCENT_BOUNDS);
        }
        else if (definition.fillMode == FillMode.LEVEL)
        {
            annotation { "Name" : "Liquid level" }
            isLength(definition.fillLevel, NONNEGATIVE_LENGTH_BOUNDS);
        }

        annotation { "Name" : "Liquid density (g/mL)" }
        isReal(definition.density, DENSITY_BOUNDS);

        annotation { "Name" : "Keep liquid body" }
        definition.keepLiquid is boolean;

        annotation { "Name" : "Store as variable" }
        definition.storeVariable is boolean;

        if (definition.storeVariable)
        {
            annotation { "Name" : "Variable name" }
            definition.variableName is string;
        }
    }
    {
        if (isQueryEmpty(context, definition.container))
            throw regenError("Select a container body.", ["container"]);

        const container = qBodyType(definition.container, BodyType.SOLID);
        const up = getUpDirection(context, definition);
        const cSys = coordSystem(WORLD_ORIGIN, perpendicularVector(up), up);

        // Container bounds in the "up" coordinate system (z = up).
        const bounds = evBox3d(context, { "topology" : container, "cSys" : cSys, "tight" : true });
        const containerHeight = bounds.maxCorner[2] - bounds.minCorner[2];
        const margin = max(1 * millimeter, 0.02 * norm(bounds.maxCorner - bounds.minCorner));

        // Pass 1: fill to the brim. Needed for FULL, and to know the cavity depth for PERCENT.
        var liquid = qNothing();
        if (definition.fillMode != FillMode.LEVEL)
        {
            liquid = buildLiquid(context, id + "full", container, cSys, bounds, bounds.maxCorner[2], margin);
            if (isQueryEmpty(context, liquid))
            {
                throw regenError("No enclosed volume found at the brim. Check the up direction, " ~
                                 "or the container may overflow below its top (spout/handle higher than the rim) - " ~
                                 "use 'Liquid level' mode in that case.", ["container", "upDirection"]);
            }
        }

        // Pass 2: partial fill.
        if (definition.fillMode == FillMode.PERCENT)
        {
            if (definition.fillPercent <= 0)
                throw regenError("Fill percentage must be greater than 0.", ["fillPercent"]);

            if (definition.fillPercent < 100)
            {
                const cavity = evBox3d(context, { "topology" : liquid, "cSys" : cSys, "tight" : true });
                const level = cavity.minCorner[2] + (definition.fillPercent / 100) * (cavity.maxCorner[2] - cavity.minCorner[2]);
                opDeleteBodies(context, id + "deleteFull", { "entities" : liquid });
                liquid = buildLiquid(context, id + "partial", container, cSys, bounds, level, margin);
            }
        }
        else if (definition.fillMode == FillMode.LEVEL)
        {
            var level = bounds.minCorner[2] + definition.fillLevel;
            if (definition.fillLevel > containerHeight)
            {
                reportFeatureWarning(context, id, "Liquid level exceeds container height; clamped to the top.");
                level = bounds.maxCorner[2];
            }
            liquid = buildLiquid(context, id + "partial", container, cSys, bounds, level, margin);
        }

        if (isQueryEmpty(context, liquid))
        {
            throw regenError("No liquid found at this level. The level may be below the inner bottom, " ~
                             "or the container leaks below it.", ["container"]);
        }

        // Measure.
        const volume = evVolume(context, { "entities" : liquid });
        const mL = volume / (centimeter ^ 3);
        const massG = mL * definition.density;
        const pieceCount = size(evaluateQuery(context, liquid));

        var message = "Liquid volume: " ~ toString(roundToPrecision(mL, 2)) ~ " mL (" ~
                      toString(roundToPrecision(mL / 1000, 4)) ~ " L), mass: " ~
                      toString(roundToPrecision(massG, 2)) ~ " g";
        if (pieceCount > 1)
            message = message ~ " [" ~ toString(pieceCount) ~ " separate cavities]";

        println(message);
        reportFeatureInfo(context, id, message);
        setFeatureComputedParameter(context, id, { "name" : "volume", "value" : volume });

        if (definition.storeVariable)
        {
            setVariable(context, definition.variableName, volume);
        }

        // Keep the liquid as a translucent blue part for visual checking, or remove it.
        if (definition.keepLiquid)
        {
            setProperty(context, { "entities" : liquid, "propertyType" : PropertyType.NAME, "value" : "Liquid" });
            setProperty(context, { "entities" : liquid, "propertyType" : PropertyType.APPEARANCE,
                                   "value" : color(0.2, 0.55, 1.0, 0.45) });
        }
        else
        {
            opDeleteBodies(context, id + "deleteLiquid", { "entities" : liquid });
        }
    }, {
        "fillMode" : FillMode.FULL,
        "fillPercent" : 80,
        "fillLevel" : 0 * meter,
        "density" : 1,
        "flipUp" : false,
        "keepLiquid" : true,
        "storeVariable" : false,
        "variableName" : "liquidVolume"
    });

/**
 * Returns the unit "up" vector: from the selected plane normal / axis, or world +Z.
 */
function getUpDirection(context is Context, definition is map) returns Vector
{
    var up = vector(0, 0, 1);
    if (!isQueryEmpty(context, definition.upDirection))
    {
        const plane = try silent(evPlane(context, { "face" : definition.upDirection }));
        if (plane != undefined)
        {
            up = plane.normal;
        }
        else
        {
            const axis = try silent(evAxis(context, { "axis" : definition.upDirection }));
            if (axis == undefined)
                throw regenError("Could not determine a direction from the selection.", ["upDirection"]);
            up = axis.direction;
        }
    }
    if (definition.flipUp)
        up = -up;
    return normalize(up);
}

/**
 * Builds the liquid body (or bodies) that fill the container up to `level`
 * (a z coordinate in `cSys`). Returns a query for the liquid bodies; may be empty.
 */
function buildLiquid(context is Context, id is Id, container is Query, cSys is CoordSystem,
    bounds is Box3d, level is ValueWithUnits, margin is ValueWithUnits) returns Query
{
    const existing = evaluateQuery(context, qEverything(EntityType.BODY));

    // Box around the container (in local coordinates), from below its bottom up to the liquid level.
    const boxMin = vector(bounds.minCorner[0] - margin, bounds.minCorner[1] - margin, bounds.minCorner[2] - margin);
    const boxMax = vector(bounds.maxCorner[0] + margin, bounds.maxCorner[1] + margin, level);

    fCuboid(context, id + "box", { "corner1" : boxMin, "corner2" : boxMax });
    const box = qCreatedBy(id + "box", EntityType.BODY);
    opTransform(context, id + "place", { "bodies" : box, "transform" : toWorld(cSys) });

    // Carve the container out of the box; the box splits into air + liquid pieces.
    opBoolean(context, id + "carve", {
                "tools" : container,
                "targets" : box,
                "operationType" : BooleanOperationType.SUBTRACTION,
                "keepTools" : true
            });

    const pieces = evaluateQuery(context, qSubtraction(qEverything(EntityType.BODY), qUnion(existing)));

    // Liquid = pieces that don't touch the box's sides or bottom (those are outside air).
    const tol = margin / 2;
    var liquid = [];
    var air = [];
    for (var piece in pieces)
    {
        const b = evBox3d(context, { "topology" : piece, "cSys" : cSys, "tight" : true });
        const enclosed = b.minCorner[0] > boxMin[0] + tol && b.maxCorner[0] < boxMax[0] - tol &&
                         b.minCorner[1] > boxMin[1] + tol && b.maxCorner[1] < boxMax[1] - tol &&
                         b.minCorner[2] > boxMin[2] + tol;
        if (enclosed)
            liquid = append(liquid, piece);
        else
            air = append(air, piece);
    }

    if (size(air) > 0)
        opDeleteBodies(context, id + "deleteAir", { "entities" : qUnion(air) });

    return qUnion(liquid);
}
